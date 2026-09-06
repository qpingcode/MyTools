using System.Windows;

namespace MyTools.Desktop.Views;

internal static class PluginWindowLayoutMetrics
{
    public const double ResizeBorderThickness = 8;
    public const double LeadingDragRegionWidth = 16;
    public const double CaptionButtonWidth = 46;
    public const int CaptionButtonCount = 3;
    public const double TitleIdentityHorizontalPadding = 16;
    public const double MinimumTitleTextWidth = 260;
    public const double CaptionButtonsWidth = CaptionButtonWidth * CaptionButtonCount;
    public const double MinimumTitleIdentityRegionWidth = MinimumTitleTextWidth + TitleIdentityHorizontalPadding;
    public const double MinimumWindowWidth =
        LeadingDragRegionWidth +
        CaptionButtonsWidth +
        MinimumTitleIdentityRegionWidth;

    public static Thickness GetPluginContentMargin(WindowState windowState, bool isStatusBarVisible)
    {
        if (windowState == WindowState.Maximized)
        {
            return new Thickness(0);
        }

        // WebView2 owns a child HWND. Keep it out of WindowChrome's resize hit-test
        // area, otherwise the child window consumes pointer input at these edges.
        return new Thickness(
            ResizeBorderThickness,
            0,
            ResizeBorderThickness,
            isStatusBarVisible ? 0 : ResizeBorderThickness);
    }

    public static int DipToDevicePixels(double dip, double dpiScale)
    {
        if (dip <= 0 || dpiScale <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(dip * dpiScale);
    }
}
