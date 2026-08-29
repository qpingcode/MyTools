using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Plugins;

namespace MyTools.Plugins;

public class Searcher(IGlobalSearchRegistry globalSearchRegistry, IMemoryCache cache, SearchHistoryDbHelper searchHistoryDbHelper, ILogger<Searcher> logger) : ISearcher
{
    private const string HomePageCacheKey = "Searcher_HomePage";
    private static readonly TimeSpan HomePageCacheDuration = TimeSpan.FromMinutes(10);

    async Task<Result> ISearcher.SearchAsync(IPlugin? plugin, string searchText, CancellationToken cancellationToken)
    {
        searchHistoryDbHelper.RecordSearch(searchText);

        if (plugin != null)
        {
            var pluginStopwatch = Stopwatch.StartNew();
            var result = await plugin.SearchAsync(searchText, cancellationToken, new SearchOptions(SearchFrom.Plugin));
            pluginStopwatch.Stop();
            logger.LogInformation(
                "Search completed: query={Query} plugin={PluginName} total={TotalMs}ms",
                searchText, plugin.Name, pluginStopwatch.ElapsedMilliseconds);
            var prepared = PrepareResultItems(result.Items, plugin, searchText).ToList();
            ApplyHistoryBoosts(prepared, searchText);
            return Result.CreateSuccessResult(
                prepared, result.EmptyStateTitle, result.EmptyStateDescription);
        }

        if (string.IsNullOrWhiteSpace(searchText)
            && cache.TryGetValue(HomePageCacheKey, out List<ResultItem>? cachedItems)
            && cachedItems != null)
        {
            return Result.CreateSuccessResult(CloneItems(cachedItems));
        }

        var searchResult = await GlobalSearchAsync(searchText, cancellationToken);
        if (string.IsNullOrWhiteSpace(searchText))
        {
            cache.Set(HomePageCacheKey, CloneItems(searchResult.Items), HomePageCacheDuration);
        }

        return searchResult;
    }

    public async Task WarmupHomePageAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(HomePageCacheKey, out _))
        {
            return;
        }

        try
        {
            var result = await GlobalSearchAsync(string.Empty, cancellationToken);
            cache.Set(HomePageCacheKey, CloneItems(result.Items), HomePageCacheDuration);
            logger.LogInformation("Home page search cache warmed with {Count} results.", result.Items.Count());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to warm home page search cache.");
        }
    }

    public void InvalidateHomePageCache()
    {
        cache.Remove(HomePageCacheKey);
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

    private IEnumerable<ResultItem> PrepareResultItems(IEnumerable<ResultItem> items, IPlugin plugin, string query)
    {
        var pluginId = plugin.PluginId.Value;
        foreach (var resultItem in items)
        {
            resultItem.AllowedActions = resultItem.AllowedActions.Any() ? resultItem.AllowedActions : plugin.Actions;
            resultItem.SourcePluginId = pluginId;
            resultItem.SourcePluginName = plugin.Name;
            resultItem.SearchQuery = query;
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

    private static List<ResultItem> CloneItems(IEnumerable<ResultItem> items)
    {
        return items.Select(item => item.Clone()).ToList();
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
