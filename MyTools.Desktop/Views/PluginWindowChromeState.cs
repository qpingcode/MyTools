using System.Windows;

namespace MyTools.Desktop.Views;

internal readonly record struct PluginWindowChromeState(
    Thickness FrameMargin,
    CornerRadius CornerRadius,
    bool ShowShadow,
    bool ShowRestoreIcon)
{
    /// <summary>
    /// 关闭按钮贴齐窗口右上角。悬停红底只圆右上角，避免盖住窗口圆角变成直角。
    /// </summary>
    public CornerRadius CloseButtonCornerRadius => new(0, CornerRadius.TopRight, 0, 0);

    public static PluginWindowChromeState From(WindowState windowState)
    {
        return windowState == WindowState.Maximized
            ? new PluginWindowChromeState(new Thickness(0), new CornerRadius(0), false, true)
            : new PluginWindowChromeState(new Thickness(10), new CornerRadius(12), true, false);
    }
}
