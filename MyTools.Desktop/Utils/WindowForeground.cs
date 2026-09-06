using System.Windows;
using System.Windows.Interop;

namespace MyTools.Desktop.Utils;

internal static class WindowForeground
{
    public static bool IsForeground(Window? window)
    {
        if (window == null || !window.IsVisible) return false;
        var handle = new WindowInteropHelper(window).Handle;
        // WPF IsActive can be true while another process owns the foreground.
        return handle != IntPtr.Zero && Native.GetForegroundWindow() == handle;
    }
}
