using System.Windows;

namespace MyTools.Desktop.Views;

internal readonly record struct PluginWindowChromeState(
    Thickness FrameMargin,
    CornerRadius CornerRadius,
    bool ShowShadow,
    bool ShowRestoreIcon)
{
    public static PluginWindowChromeState From(WindowState windowState)
    {
        return windowState == WindowState.Maximized
            ? new PluginWindowChromeState(new Thickness(0), new CornerRadius(0), false, true)
            : new PluginWindowChromeState(new Thickness(10), new CornerRadius(12), true, false);
    }
}
