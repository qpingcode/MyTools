using Microsoft.Extensions.DependencyInjection;
using MyTools.Common.Plugins;
using MyTools.Desktop.Views;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

/// <summary>
/// 插件独立窗口的注册表。负责「同一插件单例、不同插件多开」：
/// - key 为 <see cref="NodePlugin.PluginId"/>，每种插件最多保留一个活跃窗口。
/// - 重复调用同一插件热键时，复用（激活/聚焦）已存在的窗口，而不是新建。
/// - 不同插件各自拥有独立窗口，互不影响。
/// </summary>
public sealed class PluginWindowManager
{
    private readonly Dictionary<PluginId, PluginWindow> windows = new();
    private readonly IServiceProvider serviceProvider;
    private readonly WindowPlacementService windowPlacement;

    public PluginWindowManager(IServiceProvider serviceProvider, WindowPlacementService windowPlacement)
    {
        this.serviceProvider = serviceProvider;
        this.windowPlacement = windowPlacement;
    }

    /// <summary>
    /// 打开或聚焦指定插件的独立窗口。
    /// </summary>
    public void ShowOrFocus(NodePlugin plugin, NodePluginDetailContext? context)
    {
        if (windows.TryGetValue(plugin.PluginId, out var existing))
        {
            // A repeated plugin hotkey means "return to the existing window". Reapplying the
            // empty hotkey context would send initialize again and discard the page's live state.
            _ = existing.ActivatePluginAsync();
            return;
        }

        var window = serviceProvider.GetRequiredService<PluginWindow>();
        window.Closed += (_, _) => windows.Remove(plugin.PluginId);
        window.SetPlugin(plugin, context);
        var placementKey = WindowPlacementService.PluginKey(plugin.PluginId.Value);
        windowPlacement.Restore(window, placementKey);
        windowPlacement.Track(window, placementKey);
        window.Show();
        _ = window.ActivatePluginAsync();

        windows[plugin.PluginId] = window;
    }

    public void RefreshOpenPlugins(IEnumerable<NodePlugin> plugins)
    {
        var current = plugins.ToDictionary(plugin => plugin.PluginId);
        foreach (var (pluginId, window) in windows.ToList())
        {
            if (!current.TryGetValue(pluginId, out var plugin))
            {
                window.Close();
                continue;
            }
            window.SetPlugin(plugin, plugin.CreateHotKeyDetailContext());
            _ = window.ActivatePluginAsync();
        }
    }

    public void RefreshOpenPlugin(string parentPluginId, IEnumerable<NodePlugin> plugins)
    {
        var current = plugins.ToDictionary(plugin => plugin.PluginId);
        foreach (var (pluginId, window) in windows.ToList())
        {
            if (!IsEntryOf(pluginId, parentPluginId))
            {
                continue;
            }

            if (!current.TryGetValue(pluginId, out var plugin))
            {
                window.Close();
                continue;
            }

            window.SetPlugin(plugin, plugin.CreateHotKeyDetailContext());
            _ = window.ActivatePluginAsync();
        }
    }

    private static bool IsEntryOf(PluginId pluginId, string parentPluginId) =>
        pluginId.Value.StartsWith(parentPluginId + ":", StringComparison.OrdinalIgnoreCase);
}
