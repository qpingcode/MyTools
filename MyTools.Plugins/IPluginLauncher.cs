namespace MyTools.Plugins;

/// <summary>
/// How a plugin was opened by <see cref="IPluginLauncher"/>.
/// </summary>
public enum PluginLaunchKind
{
    NotFound,
    PluginWindow,
    SearchWindow
}

/// <summary>
/// Opens a registered plugin the same way its hotkey would:
/// a dedicated plugin window when the entry has a web detail page, otherwise the search window locked to that plugin.
/// </summary>
public interface IPluginLauncher
{
    PluginLaunchKind Open(string pluginId);
    PluginLaunchKind Open(IPlugin plugin);
}
