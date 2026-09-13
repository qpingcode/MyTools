using System.Windows;
using MyTools.Desktop.Services;

namespace MyTools.Desktop.Views;

internal static class PluginDockWindowArrangement
{
    public static DipRect CaptureBounds(PluginWindow window)
    {
        if (window.WindowState != WindowState.Normal)
        {
            var restore = window.RestoreBounds;
            if (restore.Width > 0 && restore.Height > 0)
            {
                return new DipRect(restore.Left, restore.Top, restore.Width, restore.Height);
            }
        }

        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
        return new DipRect(window.Left, window.Top, width, height);
    }

    public static DipRect Place(DipRect first, int index)
    {
        var offset = PluginDockLayoutMetrics.ArrangeCascadeStep * index;
        return new DipRect(first.Left + offset, first.Top + offset, first.Width, first.Height);
    }
}
