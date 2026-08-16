using System.Windows.Input;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Desktop.Models;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

/// <summary>
/// 管理插件热键的注册/重注册、关键词的重注册、以及冲突检测。
/// 热键 ID 映射存储在 pluginId→hotKeyId 字典中，支持 unregister→re-register。
/// </summary>
public sealed class KeymapService
{
    private readonly HotKeyManager hotKeyManager;
    private readonly KeymapOverrideProvider overrideProvider;
    private readonly IKeywordRegistry keywordRegistry;
    private readonly ILogger<KeymapService> logger;

    // pluginId → Win32 hotkey id，用于 unregister
    private readonly Dictionary<string, int> hotKeyIds = new();

    // 热键回调表：pluginId → callback，用于 re-register 时复用
    private readonly Dictionary<string, Action> hotKeyCallbacks = new();

    public KeymapService(
        HotKeyManager hotKeyManager,
        KeymapOverrideProvider overrideProvider,
        IKeywordRegistry keywordRegistry,
        ILogger<KeymapService> logger)
    {
        this.hotKeyManager = hotKeyManager;
        this.overrideProvider = overrideProvider;
        this.keywordRegistry = keywordRegistry;
        this.logger = logger;
    }

    /// <summary>
    /// 注册所有 Node 插件的热键（启动时调用）。
    /// 查覆盖层，有覆盖用覆盖值，否则用 plugin.json 默认值。
    /// 跳过被禁用的插件。
    /// </summary>
    public void RegisterAllHotKeys(IEnumerable<NodePlugin> nodePlugins, Action<NodePlugin> openDetail)
    {
        foreach (var plugin in nodePlugins)
        {
            // 应用启用状态和全局结果覆盖
            ApplyOverridesToPlugin(plugin);

            if (!plugin.IsEnabled)
            {
                continue;
            }

            var hotKeyText = overrideProvider.GetHotKey(plugin.PluginId) ?? plugin.HotKey;
            if (string.IsNullOrWhiteSpace(hotKeyText))
            {
                continue;
            }

            var callback = () => openDetail(plugin);
            hotKeyCallbacks[plugin.PluginId] = callback;
            RegisterSingleHotKey(plugin.PluginId, hotKeyText, callback);
        }
    }

    private void RegisterSingleHotKey(string pluginId, string hotKeyText, Action callback)
    {
        var hotKey = new HotKeyConfig(hotKeyText);
        if (hotKey.Key == Key.None || hotKey.Modifiers == ModifierKeys.None)
        {
            return;
        }

        try
        {
            var id = hotKeyManager.RegisterHotKey(hotKey.Key, hotKey.Modifiers, callback);
            hotKeyIds[pluginId] = id;
            logger.LogInformation("Registered hotkey {HotKey} for plugin {PluginId}.", hotKeyText, pluginId);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Cannot register hotkey {HotKey} for plugin {PluginId}.", hotKeyText, pluginId);
        }
    }

    /// <summary>
    /// 重注册所有插件热键（保存覆盖后调用）。先 unregister 全部，再按新配置注册。
    /// </summary>
    public void ReRegisterAllHotKeys(IEnumerable<NodePlugin> nodePlugins, Action<NodePlugin> openDetail)
    {
        // Unregister 全部已有热键
        foreach (var id in hotKeyIds.Values)
        {
            hotKeyManager.UnregisterHotKey(id);
        }
        hotKeyIds.Clear();
        hotKeyCallbacks.Clear();

        RegisterAllHotKeys(nodePlugins, openDetail);
    }

    /// <summary>
    /// 重新注册 Node 插件关键词（保存覆盖后或启动时调用）。
    /// 只替换 Node 插件条目，保留内置插件关键词。
    /// </summary>
    public void ReRegisterKeywords(IEnumerable<IPlugin> allPlugins)
    {
        foreach (var plugin in allPlugins.OfType<NodePlugin>())
        {
            keywordRegistry.UnregisterPlugin(plugin);

            if (!plugin.IsEnabled)
            {
                continue;
            }

            var keywords = overrideProvider.GetKeywords(plugin.PluginId) ?? plugin.Keywords.ToList();
            foreach (var keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    keywordRegistry.Register(keyword, plugin);
                }
            }
        }
    }

    /// <summary>
    /// 应用启用状态和全局结果覆盖到 Node 插件。
    /// </summary>
    public void ApplyEnabledOverrides(IEnumerable<NodePlugin> nodePlugins)
    {
        foreach (var plugin in nodePlugins)
        {
            ApplyOverridesToPlugin(plugin);
        }
    }

    private void ApplyOverridesToPlugin(NodePlugin plugin)
    {
        var enabled = overrideProvider.GetIsEnabled(plugin.PluginId);
        if (enabled.HasValue)
        {
            plugin.IsEnabled = enabled.Value;
        }

        var include = overrideProvider.GetIncludeInGlobalResults(plugin.PluginId);
        plugin.IsGlobalSearchPlugin = include ?? plugin.DefaultIncludeInGlobalResults;
    }

    /// <summary>
    /// 检测热键冲突：在给定的覆盖集合中，是否有两个插件用了相同的热键。
    /// 返回冲突列表（pluginId, 冲突的对方 pluginId）。
    /// </summary>
    public List<KeymapConflict> ValidateHotKeys(
        IReadOnlyDictionary<string, string?> pendingHotKeys,
        IReadOnlyDictionary<string, string> pluginNames,
        IReadOnlyDictionary<string, string?> currentHotKeys)
    {
        var conflicts = new List<KeymapConflict>();

        // 合并：pending 覆盖优先，否则用当前值
        var resolved = new Dictionary<string, string?>(currentHotKeys);
        foreach (var (pluginId, hotKey) in pendingHotKeys)
        {
            resolved[pluginId] = hotKey;
        }

        // 按热键分组（忽略 null/空）
        var byHotKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pluginId, hotKey) in resolved)
        {
            if (string.IsNullOrWhiteSpace(hotKey))
            {
                continue;
            }

            if (!byHotKey.TryGetValue(hotKey, out var list))
            {
                list = new List<string>();
                byHotKey[hotKey] = list;
            }
            list.Add(pluginId);
        }

        foreach (var (hotKey, pluginIds) in byHotKey)
        {
            if (pluginIds.Count <= 1)
            {
                continue;
            }

            foreach (var pluginId in pluginIds)
            {
                var conflictWith = pluginIds.First(id => id != pluginId);
                conflicts.Add(new KeymapConflict(
                    pluginId,
                    "hotKey",
                    hotKey,
                    conflictWith,
                    pluginNames.GetValueOrDefault(conflictWith, conflictWith)));
            }
        }

        return conflicts;
    }

    /// <summary>
    /// 检测关键词冲突。
    /// </summary>
    public List<KeymapConflict> ValidateKeywords(
        IReadOnlyDictionary<string, List<string>?> pendingKeywords,
        IReadOnlyDictionary<string, string> pluginNames,
        IReadOnlyDictionary<string, List<string>?> currentKeywords)
    {
        var conflicts = new List<KeymapConflict>();

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

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (!byKeyword.TryGetValue(keyword, out var list))
                {
                    list = new List<string>();
                    byKeyword[keyword] = list;
                }
                list.Add(pluginId);
            }
        }

        foreach (var (keyword, pluginIds) in byKeyword)
        {
            if (pluginIds.Count <= 1)
            {
                continue;
            }

            foreach (var pluginId in pluginIds)
            {
                var conflictWith = pluginIds.First(id => id != pluginId);
                conflicts.Add(new KeymapConflict(
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

/// <summary>
/// 冲突描述：某个插件的某个字段值与另一个插件冲突。
/// </summary>
public sealed record KeymapConflict(
    string PluginId,
    string Field,
    string Value,
    string ConflictsWithId,
    string ConflictsWithName);
