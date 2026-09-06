using System.Windows;

namespace MyTools.Desktop.Views;

internal readonly record struct PluginWindowChromeState(
    CornerRadius CornerRadius,
    bool ShowRestoreIcon)
{
    /// <summary>
    /// 关闭按钮贴齐窗口右上角。悬停红底只圆右上角，避免盖住窗口圆角变成直角。
    /// </summary>
    public CornerRadius CloseButtonCornerRadius => new(0, CornerRadius.TopRight, 0, 0);

    public static PluginWindowChromeState From(WindowState windowState)
    {
        return windowState == WindowState.Maximized
            ? new PluginWindowChromeState(new CornerRadius(0), true)
            : new PluginWindowChromeState(new CornerRadius(12), false);
    }
}
