using System.Windows;

namespace MyTools.Desktop.Views;

/// <summary>
/// Keeps a window above others only while it is focused. Always-on-top overlays
/// (for example the mouse-trail window) should not use this.
/// </summary>
internal static class WindowFocusTopmost
{
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;
        if (window.IsActive)
        {
            window.Topmost = true;
        }
    }

    internal static void OnActivated(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Topmost = true;
        }
    }

    internal static void OnDeactivated(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Topmost = false;
        }
    }
}
