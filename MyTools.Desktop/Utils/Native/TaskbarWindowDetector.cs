using System.Text;

namespace MyTools.Desktop.Utils;

internal static class TaskbarWindowDetector
{
    private const uint GetAncestorRoot = 2;
    private const int MaxClassNameLength = 256;

    public static bool IsTaskbarAt(Native.POINT screenPoint)
    {
        var window = Native.WindowFromPoint(screenPoint);
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var rootWindow = Native.GetAncestor(window, GetAncestorRoot);
        if (rootWindow != IntPtr.Zero)
        {
            window = rootWindow;
        }

        var className = new StringBuilder(MaxClassNameLength);
        return Native.GetClassName(window, className, className.Capacity) > 0
               && IsTaskbarClassName(className.ToString());
    }

    internal static bool IsTaskbarClassName(string className)
        => string.Equals(className, "Shell_TrayWnd", StringComparison.Ordinal)
           || string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.Ordinal);
}
