using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MyTools.Common.Theming;

namespace MyTools.Desktop.Themes;

internal static class WindowTitleBarTheme
{
    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;

    public static void Apply(Window window, ThemeKind theme)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var darkMode = theme == ThemeKind.Dark ? 1 : 0;
        if (TrySetWindowAttribute(handle, DwmaUseImmersiveDarkMode, darkMode) != 0)
        {
            _ = TrySetWindowAttribute(handle, DwmaUseImmersiveDarkModeBefore20H1, darkMode);
        }
    }

    private static int TrySetWindowAttribute(IntPtr handle, int attribute, int value)
    {
        return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
