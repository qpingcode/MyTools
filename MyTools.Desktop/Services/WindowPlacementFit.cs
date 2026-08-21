namespace MyTools.Desktop.Services;

internal readonly record struct DipRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public double CenterX => Left + (Width / 2);
    public double CenterY => Top + (Height / 2);

    public DipRect Intersect(DipRect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        if (right <= left || bottom <= top)
        {
            return new DipRect(0, 0, 0, 0);
        }

        return new DipRect(left, top, right - left, bottom - top);
    }
}

internal readonly record struct DisplayWorkArea(string DeviceName, DipRect Bounds, bool IsPrimary);

/// <summary>
/// Converts window rectangles between virtual-desktop DIP and per-monitor work-area space.
/// </summary>
internal static class WindowPlacementFit
{
    public static DipRect ToRelative(DipRect absolute, DipRect work)
    {
        return new DipRect(absolute.Left - work.Left, absolute.Top - work.Top, absolute.Width, absolute.Height);
    }

    public static DipRect FromRelative(
        DipRect relative,
        DipRect work,
        double minWidth = 0,
        double minHeight = 0)
    {
        var absolute = new DipRect(
            work.Left + relative.Left,
            work.Top + relative.Top,
            relative.Width,
            relative.Height);
        return PlaceOnWorkArea(absolute, work, minWidth, minHeight);
    }

    public static DipRect CenterOn(
        DipRect work,
        double width,
        double height,
        double minWidth = 0,
        double minHeight = 0)
    {
        width = ClampSize(width, minWidth, work.Width);
        height = ClampSize(height, minHeight, work.Height);
        var left = work.Left + Math.Max(0, (work.Width - width) / 2);
        var top = work.Top + Math.Max(0, (work.Height - height) / 2);
        return new DipRect(left, top, width, height);
    }

    internal static DipRect PlaceOnWorkArea(DipRect saved, DipRect work, double minWidth, double minHeight)
    {
        saved = EnsureMinimumSize(saved, minWidth, minHeight);
        var width = ClampSize(saved.Width, minWidth, work.Width);
        var height = ClampSize(saved.Height, minHeight, work.Height);
        var left = saved.Left;
        if (left < work.Left)
        {
            left = work.Left;
        }

        if (left + width > work.Right)
        {
            left = work.Right - width;
        }

        var top = saved.Top;
        if (top < work.Top)
        {
            top = work.Top;
        }

        if (top + height > work.Bottom)
        {
            top = work.Bottom - height;
        }

        return new DipRect(left, top, width, height);
    }

    private static DipRect EnsureMinimumSize(DipRect saved, double minWidth, double minHeight)
    {
        var width = saved.Width < minWidth ? minWidth : saved.Width;
        var height = saved.Height < minHeight ? minHeight : saved.Height;
        return saved with { Width = width, Height = height };
    }

    private static double ClampSize(double size, double min, double max)
    {
        if (max <= 0)
        {
            return Math.Max(size, min);
        }

        var lower = min > 0 && min <= max ? min : Math.Min(size, max);
        if (size < lower)
        {
            return lower;
        }

        return size > max ? max : size;
    }
}
