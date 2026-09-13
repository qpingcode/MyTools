using System.Windows;

namespace MyTools.Desktop.Views;

internal static class PluginDockLayoutMetrics
{
    public const double TileSize = 44;
    public const double TileCornerRadius = 10;
    public const double TileGap = 6;
    public const double HandleHeight = 18;
    public const double HandleGripDotSize = 3;
    public const double HandleGripDotGap = 4;
    public const double FramePadding = 6;
    public const double FrameCornerRadius = 12;
    public const double WorkAreaMargin = 12;
    public const double PositionSnapTolerance = 0.5;
    public static CornerRadius FrameCorner { get; } = new(FrameCornerRadius);
    public static Thickness FramePaddingThickness { get; } = new(FramePadding);
    public const string PinIconName = "pin";
    public const double PinIconFontSize = 16;
    public static CornerRadius TileCorner { get; } = new(TileCornerRadius);
    public static Thickness TileMargin { get; } = new(0, TileGap, 0, 0);
    public static Thickness HandleGripDotSpacing { get; } = new(0, 0, HandleGripDotGap, 0);

    public static double MinimumWidth => FramePadding + FramePadding + TileSize;

    public static double MinimumHeight => FramePadding + FramePadding + HandleHeight + TileGap + TileSize;

    public static Services.DipRect DefaultPlacement(Services.DipRect work, double width, double height)
    {
        if (width <= 0)
        {
            width = MinimumWidth;
        }

        if (height <= 0)
        {
            height = MinimumHeight;
        }

        var left = work.Left + WorkAreaMargin;
        var top = work.Bottom - WorkAreaMargin - height;
        return Services.WindowPlacementFit.PlaceOnWorkArea(
            new Services.DipRect(left, top, width, height),
            work,
            width,
            height);
    }
}
