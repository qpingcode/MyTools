using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Common.Localization;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Plugins;

public class Searcher(IGlobalSearchRegistry globalSearchRegistry, SearchHistoryDbHelper searchHistoryDbHelper, ILogger<Searcher> logger, IEnumerable<IPlugin>? builtInPlugins = null) : ISearcher
{
    async Task<Result> ISearcher.SearchAsync(IPlugin? plugin, string searchText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        searchHistoryDbHelper.RecordSearch(searchText);

        if (plugin != null)
        {
            var pluginStopwatch = Stopwatch.StartNew();
            var result = await plugin.SearchAsync(searchText, cancellationToken, new SearchOptions(SearchFrom.Plugin));
            pluginStopwatch.Stop();
            logger.LogInformation(
                "Search completed: query={Query} plugin={PluginName} total={TotalMs}ms",
                searchText, plugin.Name, pluginStopwatch.ElapsedMilliseconds);
            var prepared = PrepareResultItems(result.Items, plugin, searchText, SearchFrom.Plugin).ToList();
            ApplyHistoryBoosts(prepared, searchText);
            return Result.CreateSuccessResult(
                prepared, result.EmptyStateTitle, result.EmptyStateDescription);
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return ReadHomePage(cancellationToken);
        }

        return await GlobalSearchAsync(searchText, cancellationToken);
    }

    private IEnumerable<IPlugin> AvailablePlugins => globalSearchRegistry.Plugins.Concat(builtInPlugins ?? [])
        .Where(plugin => plugin.IsEnabled && (plugin is not NodePlugin node || node.HasInstalledEntry))
        .DistinctBy(plugin => plugin.PluginId.Value);

    private Result ReadHomePage(CancellationToken cancellationToken)
    {
        var plugins = AvailablePlugins.ToDictionary(plugin => plugin.PluginId.Value);
        var items = new List<ResultItem>();
        foreach (var entry in searchHistoryDbHelper.GetRecentSelections())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Snapshot is not { IsCurrent: true, Actions.Count: > 0 } snapshot
                || !plugins.TryGetValue(entry.PluginId, out var plugin)) continue;
            try
            {
                var arguments = snapshot.RestoreArguments() ?? snapshot;
                var actions = RestoreHistoryActions(plugin, entry, snapshot);
                items.Add(new ResultItem(snapshot.RestoreIcon(), snapshot.Title, snapshot.SubTitle, arguments)
                {
                    SourcePluginId = entry.PluginId,
                    SourcePluginName = plugin.Name,
                    ResultKey = entry.ResultKey,
                    SearchQuery = entry.Query,
                    SearchFrom = entry.SearchFrom,
                    AllowedActions = actions
                });
                if (items.Count == 50) break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Invalid snapshot for {PluginId}/{ResultKey}.", entry.PluginId, entry.ResultKey);
            }
        }
        return new Result(true, null, items);
    }

    private IReadOnlyList<IActionWithHotkey> RestoreHistoryActions(
        IPlugin plugin,
        SearchHistorySelection entry,
        SearchResultSnapshot snapshot)
    {
        var currentActions = plugin.Actions
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return snapshot.Actions!.Select(saved =>
        {
            if (snapshot.RestoreArguments() != null && currentActions.TryGetValue(saved.Id, out var current))
            {
                return current;
            }

            IActionWithHotkey action = new ActionWithHotkey(
                new OpenHistoryResultAction(this, entry, saved.Id, saved.Name, saved.Description),
                saved.Hotkey,
                saved.Pinned);
            return action;
        }).ToArray();
    }

    private sealed class OpenHistoryResultAction(
        Searcher owner,
        SearchHistorySelection entry,
        string actionId,
        string? name = null,
        string? description = null) : IAction
    {
        public string Id => actionId;
        public string Name => name ?? actionId;
        public string Description => description ?? Name;
        public Task<ActionResult> ExecuteAsync(IActionParams args) => owner.ExecuteHistoryResultAsync(entry, actionId);
    }

    private async Task<ActionResult> ExecuteHistoryResultAsync(SearchHistorySelection entry, string actionId)
    {
        try
        {
            var plugin = AvailablePlugins.FirstOrDefault(plugin => plugin.PluginId.Value == entry.PluginId);
            if (plugin != null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = await plugin.SearchAsync(entry.Query, timeout.Token, new SearchOptions(entry.SearchFrom))
                    .WaitAsync(timeout.Token);
                var selected = result.Success
                    ? PrepareResultItems(result.Items, plugin, entry.Query, entry.SearchFrom)
                        .FirstOrDefault(item => item.ResultKey == entry.ResultKey)
                    : null;
                var action = selected?.AllowedActions.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, actionId, StringComparison.Ordinal));
                if (selected != null && action != null && AvailablePlugins.Contains(plugin))
                {
                    var outcome = await action.ExecuteAsync(selected.Args);
                    if (outcome.Success)
                    {
                        searchHistoryDbHelper.UpdateSnapshot(selected);
                        return outcome;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Historical result is unavailable: {PluginId}/{ResultKey}.", entry.PluginId, entry.ResultKey);
        }
        searchHistoryDbHelper.InvalidateSnapshot(entry.PluginId, entry.ResultKey);
        return ActionResult.CreateFailure(new LocalizedMessage(
            "Search.History.Unavailable", "This result is no longer available. Search again to find an updated result."), ActionTypeEnum.Refresh);
    }

    private async Task<Result> GlobalSearchAsync(string query, CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();

        var tasks = globalSearchRegistry.Plugins
            .Where(p => p.IsEnabled && p.IsGlobalSearchPlugin)
            .Select(async plugin =>
            {
                var pluginStopwatch = Stopwatch.StartNew();
                try
                {
                    var result = await plugin.SearchAsync(query, cancellationToken, new SearchOptions(SearchFrom.Global));
                    pluginStopwatch.Stop();
                    return (Plugin: plugin, Result: result, ElapsedMs: pluginStopwatch.ElapsedMilliseconds);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    pluginStopwatch.Stop();
                    logger.LogError(ex, "Global search failed for plugin {PluginName}.", plugin.Name);
                    return (Plugin: plugin, Result: Result.CreateFailure(ex.Message, ex), ElapsedMs: pluginStopwatch.ElapsedMilliseconds);
                }
            })
            .ToList();

        var results = await Task.WhenAll(tasks);
        totalStopwatch.Stop();

        var breakdown = string.Join(", ", results
            .OrderByDescending(r => r.ElapsedMs)
            .Select(r => $"{r.Plugin.Name}:{r.ElapsedMs}ms"));
        var failedPlugins = string.Join(", ", results
            .Where(r => !r.Result.Success)
            .Select(r => r.Plugin.Name));
        if (failedPlugins.Length == 0)
        {
            logger.LogInformation(
                "Search completed: query={Query} total={TotalMs}ms plugins=[{PluginBreakdown}]",
                query, totalStopwatch.ElapsedMilliseconds, breakdown);
        }
        else
        {
            logger.LogWarning(
                "Search completed with failures: query={Query} total={TotalMs}ms plugins=[{PluginBreakdown}] failed=[{FailedPlugins}]",
                query, totalStopwatch.ElapsedMilliseconds, breakdown, failedPlugins);
        }

        var items = results
            .Where(pair => pair.Result.Success)
            .SelectMany(pair => PrepareResultItems(pair.Result.Items, pair.Plugin, query))
            .ToList();

        ApplyHistoryBoosts(items, query);
        return Result.CreateSuccessResult(items);
    }

    private IEnumerable<ResultItem> PrepareResultItems(IEnumerable<ResultItem> items, IPlugin plugin, string query, SearchFrom searchFrom = SearchFrom.Global)
    {
        var pluginId = plugin.PluginId.Value;
        foreach (var source in items)
        {
            var resultItem = source.Clone();
            resultItem.AllowedActions = resultItem.AllowedActions.Any() ? resultItem.AllowedActions : plugin.Actions;
            resultItem.SourcePluginId = pluginId;
            resultItem.SourcePluginName = plugin.Name;
            resultItem.SearchQuery = query;
            resultItem.SearchFrom = searchFrom;
            resultItem.ResultKey = string.IsNullOrWhiteSpace(resultItem.ResultKey)
                ? BuildResultKey(resultItem)
                : resultItem.ResultKey;
            resultItem.SortScore = resultItem.Priority;
            yield return resultItem;
        }
    }

    private void ApplyHistoryBoosts(IEnumerable<ResultItem> items, string query)
    {
        var boosts = searchHistoryDbHelper.GetSelectionBoosts(query);
        foreach (var item in items)
        {
            var key = SearchHistoryDbHelper.CombineKey(item.SourcePluginId, item.ResultKey);
            item.SortScore = item.IgnoreSelectionHistoryBoost
                ? item.Priority
                : item.Priority + boosts.GetValueOrDefault(key, 0);
        }
    }

    private static string BuildResultKey(ResultItem item)
    {
        var builder = new StringBuilder();
        builder.Append(item.Title);
        builder.Append('|');
        builder.Append(item.SubTitle);
        builder.Append('|');
        builder.Append(item.Args.GetType().FullName);

        if (item.Args is IActionStringParam stringParam)
        {
            builder.Append('|');
            builder.Append(stringParam.GetValue());
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hashBytes);
    }
}
