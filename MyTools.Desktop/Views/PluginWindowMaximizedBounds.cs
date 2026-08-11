namespace MyTools.Desktop.Views;

internal readonly record struct PluginWindowNativeRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

internal readonly record struct PluginWindowMaximizedBounds(int PositionX, int PositionY, int Width, int Height)
{
    public static PluginWindowMaximizedBounds FromMonitorInfo(
        PluginWindowNativeRect monitorArea,
        PluginWindowNativeRect workArea)
    {
        return new PluginWindowMaximizedBounds(
            workArea.Left - monitorArea.Left,
            workArea.Top - monitorArea.Top,
            workArea.Width,
            workArea.Height);
    }
}
