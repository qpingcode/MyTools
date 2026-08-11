using System.Windows;

namespace MyTools.Desktop.Views;

internal enum PluginWindowActivationRestoreAction
{
    None,
    NativeRestore
}

internal static class PluginWindowActivationRestoreDecision
{
    public static PluginWindowActivationRestoreAction From(WindowState windowState)
    {
        return windowState == WindowState.Minimized
            ? PluginWindowActivationRestoreAction.NativeRestore
            : PluginWindowActivationRestoreAction.None;
    }
}
