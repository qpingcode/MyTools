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
    private readonly HashSet<PluginId> hotKeyActivated = new();
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
    public void ShowOrFocus(NodePlugin plugin, NodePluginDetailContext? context, bool replaceContext = false)
    {
        if (windows.TryGetValue(plugin.PluginId, out var existing))
        {
            if (replaceContext || !existing.HasPluginContent)
            {
                existing.SetPlugin(plugin, context);
            }
            _ = existing.ActivatePluginAsync(requestForeground: !hotKeyActivated.Remove(plugin.PluginId));
            return;
        }

        var window = CreateAndShowShell(plugin, context);
        _ = window.ActivatePluginAsync();
        window.SetPlugin(plugin, context);
    }

    // Called synchronously during WM_HOTKEY. No plugin detail view is attached here.
    internal void ActivateHotKeyShell(NodePlugin plugin)
    {
        if (!windows.TryGetValue(plugin.PluginId, out var window))
        {
            var context = plugin.CreateHotKeyDetailContext();
            if (context == null) return; // Search-only plugins keep the normal deferred path.
            window = CreateAndShowShell(plugin, context);
        }
        window.ActivateShellFromHotKey();
        hotKeyActivated.Add(plugin.PluginId);
    }

    private PluginWindow CreateAndShowShell(NodePlugin plugin, NodePluginDetailContext? context)
    {
        var window = serviceProvider.GetRequiredService<PluginWindow>();
        window.Closed += (_, _) =>
        {
            windows.Remove(plugin.PluginId);
            hotKeyActivated.Remove(plugin.PluginId);
        };
        // Show and activate the shell before creating the WebView2 visual tree.
        window.PreparePluginShell(plugin, context);
        var placementKey = WindowPlacementService.PluginKey(plugin.PluginId.Value);
        windowPlacement.Restore(window, placementKey);
        windowPlacement.Track(window, placementKey);
        windows[plugin.PluginId] = window;
        try
        {
            window.Show();
            return window;
        }
        catch
        {
            windows.Remove(plugin.PluginId);
            window.Close();
            throw;
        }
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

    public void RefreshOpenPlugin(string pluginId, IEnumerable<NodePlugin> plugins)
    {
        var id = new PluginId(pluginId);
        if (!windows.TryGetValue(id, out var window))
        {
            return;
        }

        var plugin = plugins.FirstOrDefault(item => item.PluginId.Equals(id));
        if (plugin is null)
        {
            window.Close();
            return;
        }

        window.SetPlugin(plugin, plugin.CreateHotKeyDetailContext());
        _ = window.ActivatePluginAsync();
    }
}
