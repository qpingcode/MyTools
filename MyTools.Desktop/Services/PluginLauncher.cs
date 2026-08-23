using System.Windows;
using MyTools.Desktop.Utils;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class PluginLauncher : IPluginLauncher
{
    private readonly PluginLoader pluginLoader;
    private readonly PluginWindowManager pluginWindowManager;

    public PluginLauncher(PluginLoader pluginLoader, PluginWindowManager pluginWindowManager)
    {
        this.pluginLoader = pluginLoader;
        this.pluginWindowManager = pluginWindowManager;
    }

    public PluginLaunchKind Open(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return PluginLaunchKind.NotFound;
        }

        return InvokeOnUi(() =>
        {
            var plugin = FindPlugin(pluginId.Trim());
            return plugin == null ? PluginLaunchKind.NotFound : OpenCore(plugin);
        });
    }

    public PluginLaunchKind Open(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        return InvokeOnUi(() => OpenCore(plugin));
    }

    private IPlugin? FindPlugin(string pluginId)
    {
        foreach (var plugin in pluginLoader.LoadedPlugins)
        {
            if (string.Equals(plugin.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                return plugin;
            }
        }

        foreach (var plugin in pluginLoader.LoadedPlugins.OfType<NodePlugin>())
        {
            if (string.Equals(plugin.ParentId, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                return plugin;
            }
        }

        return null;
    }

    private PluginLaunchKind OpenCore(IPlugin plugin)
    {
        if (plugin is NodePlugin nodePlugin)
        {
            var context = nodePlugin.CreateHotKeyDetailContext();
            if (context != null)
            {
                pluginWindowManager.ShowOrFocus(nodePlugin, context);
                return PluginLaunchKind.PluginWindow;
            }
        }

        // This launch may originate from another plugin's result list (for example the plugin
        // searcher). Its query belongs to that source plugin and must not become the target
        // plugin's initial query.
        WindowHelper.ShowSearchWindow(plugin, string.Empty);
        return PluginLaunchKind.SearchWindow;
    }

    private static PluginLaunchKind InvokeOnUi(Func<PluginLaunchKind> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.Invoke(action);
    }
}
