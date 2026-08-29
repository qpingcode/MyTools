using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class PluginKeymapService
{
    private readonly PluginOverrideProvider overrideProvider;
    private readonly IKeywordRegistry keywordRegistry;

    public PluginKeymapService(
        PluginOverrideProvider overrideProvider,
        IKeywordRegistry keywordRegistry)
    {
        this.overrideProvider = overrideProvider;
        this.keywordRegistry = keywordRegistry;
    }

    public void ReRegisterKeywords(IEnumerable<IPlugin> allPlugins)
    {
        foreach (var plugin in allPlugins.OfType<NodePlugin>())
        {
            keywordRegistry.UnregisterPlugin(plugin);
            var keywords = overrideProvider.GetKeywords(plugin.OverrideKey, plugin.PluginId) ?? plugin.Keywords.ToList();
            plugin.SetEffectiveKeywords(keywords);

            if (!plugin.IsEnabled)
            {
                continue;
            }

            foreach (var keyword in keywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword)))
            {
                keywordRegistry.Register(keyword, plugin);
            }
        }
    }

    public void ApplyOverrides(IEnumerable<NodePlugin> nodePlugins)
    {
        var plugins = nodePlugins.ToList();
        var duplicateIds = plugins
            .GroupBy(plugin => plugin.PluginId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var plugin in plugins)
        {
            // A legacy plugin-id override is ambiguous for duplicate installations.
            // Only an explicit installation override may enable one of them.
            var enabled = duplicateIds.Contains(plugin.PluginId)
                ? overrideProvider.GetIsEnabled(plugin.OverrideKey)
                : overrideProvider.GetIsEnabled(plugin.OverrideKey, plugin.PluginId);
            if (enabled.HasValue)
            {
                plugin.IsEnabled = enabled.Value;
            }
            else if (duplicateIds.Contains(plugin.PluginId))
            {
                plugin.IsEnabled = false;
            }

            plugin.IsGlobalSearchPlugin = overrideProvider.GetIncludeInGlobalResults(plugin.OverrideKey, plugin.PluginId)
                ?? plugin.DefaultIncludeInGlobalResults;
        }

        foreach (var group in plugins
                     .Where(plugin => duplicateIds.Contains(plugin.PluginId))
                     .GroupBy(plugin => plugin.PluginId))
        {
            var enabledPlugins = group.Where(plugin => plugin.IsEnabled).ToList();
            if (enabledPlugins.Count <= 1)
            {
                continue;
            }

            foreach (var plugin in enabledPlugins)
            {
                plugin.IsEnabled = false;
            }
        }
    }

    public List<PluginOverrideConflict> ValidateKeywords(
        IReadOnlyDictionary<string, List<string>?> pendingKeywords,
        IReadOnlyDictionary<string, string> pluginNames,
        IReadOnlyDictionary<string, List<string>?> currentKeywords)
    {
        var resolved = new Dictionary<string, List<string>?>(currentKeywords);
        foreach (var (pluginId, keywords) in pendingKeywords)
        {
            resolved[pluginId] = keywords;
        }

        var byKeyword = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pluginId, keywords) in resolved)
        {
            if (keywords == null)
            {
                continue;
            }

            foreach (var keyword in keywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword)))
            {
                if (!byKeyword.TryGetValue(keyword, out var pluginIds))
                {
                    pluginIds = [];
                    byKeyword[keyword] = pluginIds;
                }
                pluginIds.Add(pluginId);
            }
        }

        var conflicts = new List<PluginOverrideConflict>();
        foreach (var (keyword, pluginIds) in byKeyword.Where(pair => pair.Value.Count > 1))
        {
            foreach (var pluginId in pluginIds)
            {
                var conflictWith = pluginIds.First(id => id != pluginId);
                conflicts.Add(new PluginOverrideConflict(
                    pluginId,
                    "keyword",
                    keyword,
                    conflictWith,
                    pluginNames.GetValueOrDefault(conflictWith, conflictWith)));
            }
        }
        return conflicts;
    }
}

public sealed record PluginOverrideConflict(
    string PluginId,
    string Field,
    string Value,
    string ConflictsWithId,
    string ConflictsWithName);
