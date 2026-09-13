using System.Windows;
using System.Text.Json;
using MyTools.Common.Plugins;
using MyTools.Desktop.Utils;
using MyTools.Desktop.Views;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class PluginLauncher : IPluginLauncher
{
    private readonly PluginLoader pluginLoader;
    private readonly PluginWindowManager pluginWindowManager;
    private readonly SearchWindow searchWindow;

    public PluginLauncher(
        PluginLoader pluginLoader,
        PluginWindowManager pluginWindowManager,
        SearchWindow searchWindow)
    {
        this.pluginLoader = pluginLoader;
        this.pluginWindowManager = pluginWindowManager;
        this.searchWindow = searchWindow;
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

    public PluginLaunchKind OpenStoreListing(string marketplacePluginId)
    {
        return InvokeOnUi(() =>
        {
            if (FindPlugin("store") is not NodePlugin store)
            {
                return PluginLaunchKind.NotFound;
            }

            var state = JsonSerializer.SerializeToElement(new { pluginId = marketplacePluginId });
            var context = store.CreateDetailContextWithState(state);
            if (context == null)
            {
                return OpenCore(store);
            }

            pluginWindowManager.ShowOrFocus(store, context, replaceContext: true);
            return PluginLaunchKind.PluginWindow;
        });
    }

    private IPlugin? FindPlugin(string pluginId)
    {
        foreach (var plugin in pluginLoader.LoadedPlugins)
        {
            if (plugin.PluginId == new PluginId(pluginId))
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

    public PluginLaunchKind DetachCurrentSearchPlugin()
    {
        return InvokeOnUi(() =>
        {
            if (searchWindow.ActivePlugin is not NodePlugin plugin)
            {
                return PluginLaunchKind.NotFound;
            }

            var context = searchWindow.CurrentDetailContext ?? plugin.CreateHotKeyDetailContext();
            if (context == null)
            {
                return PluginLaunchKind.SearchWindow;
            }

            pluginWindowManager.ShowOrFocus(plugin, context, replaceContext: true);
            searchWindow.Hide();
            return PluginLaunchKind.PluginWindow;
        });
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
