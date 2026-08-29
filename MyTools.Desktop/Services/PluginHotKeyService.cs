using System.Windows.Input;
using Microsoft.Extensions.Logging;
using MyTools.Common.Plugins;
using MyTools.Desktop.Models;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class PluginHotKeyService
{
    private readonly HotKeyManager hotKeyManager;
    private readonly PluginOverrideProvider overrideProvider;
    private readonly ILogger<PluginHotKeyService> logger;
    private readonly Dictionary<PluginId, int> hotKeyIds = new();

    public PluginHotKeyService(
        HotKeyManager hotKeyManager,
        PluginOverrideProvider overrideProvider,
        ILogger<PluginHotKeyService> logger)
    {
        this.hotKeyManager = hotKeyManager;
        this.overrideProvider = overrideProvider;
        this.logger = logger;
    }

    public void RegisterAll(IEnumerable<NodePlugin> nodePlugins, Action<NodePlugin> openDetail)
    {
        foreach (var plugin in nodePlugins)
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            var hotKeyText = overrideProvider.GetHotKey(plugin.OverrideKey, plugin.PluginId) ?? plugin.HotKey;
            if (string.IsNullOrWhiteSpace(hotKeyText))
            {
                continue;
            }

            var hotKey = new HotKeyConfig(hotKeyText);
            if (hotKey.Key == Key.None || hotKey.Modifiers == ModifierKeys.None)
            {
                continue;
            }

            try
            {
                var id = hotKeyManager.RegisterHotKey(
                    hotKey.Key,
                    hotKey.Modifiers,
                    () => openDetail(plugin));
                hotKeyIds[plugin.PluginId] = id;
                logger.LogInformation("Registered hotkey {HotKey} for plugin {PluginId}.", hotKeyText, plugin.PluginId);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "Cannot register hotkey {HotKey} for plugin {PluginId}.", hotKeyText, plugin.PluginId);
            }
        }
    }

    public void ReRegisterAll(IEnumerable<NodePlugin> nodePlugins, Action<NodePlugin> openDetail)
    {
        foreach (var id in hotKeyIds.Values)
        {
            hotKeyManager.UnregisterHotKey(id);
        }
        hotKeyIds.Clear();
        RegisterAll(nodePlugins, openDetail);
    }

    public void ReRegisterPlugin(
        string parentPluginId,
        IEnumerable<NodePlugin> nodePlugins,
        Action<NodePlugin> openDetail)
    {
        var affectedIds = hotKeyIds.Keys
            .Where(pluginId => IsEntryOf(pluginId, parentPluginId))
            .ToList();
        foreach (var pluginId in affectedIds)
        {
            hotKeyManager.UnregisterHotKey(hotKeyIds[pluginId]);
            hotKeyIds.Remove(pluginId);
        }

        RegisterAll(nodePlugins, openDetail);
    }

    private static bool IsEntryOf(PluginId pluginId, string parentPluginId) =>
        pluginId.Value.StartsWith(parentPluginId + ":", StringComparison.OrdinalIgnoreCase);

    public List<PluginOverrideConflict> Validate(
        IReadOnlyDictionary<string, string?> pendingHotKeys,
        IReadOnlyDictionary<string, string> pluginNames,
        IReadOnlyDictionary<string, string?> currentHotKeys)
    {
        var resolved = new Dictionary<string, string?>(currentHotKeys);
        foreach (var (pluginId, hotKey) in pendingHotKeys)
        {
            resolved[pluginId] = hotKey;
        }

        var byHotKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pluginId, hotKey) in resolved)
        {
            if (string.IsNullOrWhiteSpace(hotKey))
            {
                continue;
            }

            if (!byHotKey.TryGetValue(hotKey, out var pluginIds))
            {
                pluginIds = [];
                byHotKey[hotKey] = pluginIds;
            }
            pluginIds.Add(pluginId);
        }

        return BuildConflicts(byHotKey, "hotKey", pluginNames);
    }

    private static List<PluginOverrideConflict> BuildConflicts(
        Dictionary<string, List<string>> values,
        string field,
        IReadOnlyDictionary<string, string> pluginNames)
    {
        var conflicts = new List<PluginOverrideConflict>();
        foreach (var (value, pluginIds) in values.Where(pair => pair.Value.Count > 1))
        {
            foreach (var pluginId in pluginIds)
            {
                var conflictWith = pluginIds.First(id => id != pluginId);
                conflicts.Add(new PluginOverrideConflict(
                    pluginId,
                    field,
                    value,
                    conflictWith,
                    pluginNames.GetValueOrDefault(conflictWith, conflictWith)));
            }
        }
        return conflicts;
    }
}
