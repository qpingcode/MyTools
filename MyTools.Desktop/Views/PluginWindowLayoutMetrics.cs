namespace MyTools.Desktop.Views;

internal static class PluginWindowLayoutMetrics
{
    public const double FrameHorizontalMargin = 10;
    public const double LeadingDragRegionWidth = 16;
    public const double CaptionButtonWidth = 46;
    public const int CaptionButtonCount = 3;
    public const double TitleIdentityHorizontalPadding = 16;
    public const double MinimumTitleTextWidth = 260;
    public const double CaptionButtonsWidth = CaptionButtonWidth * CaptionButtonCount;
    public const double MinimumTitleIdentityRegionWidth = MinimumTitleTextWidth + TitleIdentityHorizontalPadding;
    public const double MinimumWindowWidth =
        (FrameHorizontalMargin * 2) +
        LeadingDragRegionWidth +
        CaptionButtonsWidth +
        MinimumTitleIdentityRegionWidth;

    public static int DipToDevicePixels(double dip, double dpiScale)
    {
        if (dip <= 0 || dpiScale <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(dip * dpiScale);
    }
}
