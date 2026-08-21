using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MyTools.Desktop.Services;

internal static class DisplayWorkAreas
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;
    private const int EffectiveDpi = 0;

    public static IReadOnlyList<DisplayWorkArea> Query()
    {
        var areas = new List<DisplayWorkArea>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, _, _, _) =>
            {
                if (TryRead(hMonitor, out var area))
                {
                    areas.Add(area);
                }

                return true;
            },
            IntPtr.Zero);
        return areas;
    }

    public static DisplayWorkArea? FromCursor()
    {
        if (!GetCursorPos(out var point))
        {
            return Primary();
        }

        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        return TryRead(monitor, out var area) ? area : Primary();
    }

    public static DisplayWorkArea? FromWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        return TryRead(monitor, out var area) ? area : null;
    }

    public static DisplayWorkArea? FromBounds(DipRect bounds)
    {
        DisplayWorkArea? best = null;
        var bestArea = 0d;
        foreach (var area in Query())
        {
            var overlap = bounds.Intersect(area.Bounds);
            var overlapArea = overlap.Width * overlap.Height;
            if (overlapArea > bestArea)
            {
                bestArea = overlapArea;
                best = area;
            }
        }

        return best;
    }

    public static DisplayWorkArea? FindByDeviceName(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        foreach (var area in Query())
        {
            if (string.Equals(area.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return area;
            }
        }

        return null;
    }

    public static DisplayWorkArea? Primary()
    {
        DisplayWorkArea? first = null;
        foreach (var area in Query())
        {
            first ??= area;
            if (area.IsPrimary)
            {
                return area;
            }
        }

        return first;
    }

    private static bool TryRead(IntPtr hMonitor, out DisplayWorkArea area)
    {
        area = default;
        if (hMonitor == IntPtr.Zero)
        {
            return false;
        }

        var info = MonitorInfoEx.Create();
        if (!GetMonitorInfo(hMonitor, ref info))
        {
            return false;
        }

        var dpiScaleX = GetScale(hMonitor, horizontal: true);
        var dpiScaleY = GetScale(hMonitor, horizontal: false);
        var work = info.WorkArea;
        area = new DisplayWorkArea(
            TrimDeviceName(info.DeviceName),
            new DipRect(
                work.Left / dpiScaleX,
                work.Top / dpiScaleY,
                (work.Right - work.Left) / dpiScaleX,
                (work.Bottom - work.Top) / dpiScaleY),
            (info.Flags & MonitorInfoPrimary) != 0);
        return !string.IsNullOrEmpty(area.DeviceName);
    }

    private static double GetScale(IntPtr hMonitor, bool horizontal)
    {
        if (GetDpiForMonitor(hMonitor, EffectiveDpi, out var dpiX, out var dpiY) != 0)
        {
            return 1;
        }

        var dpi = horizontal ? dpiX : dpiY;
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    private static string TrimDeviceName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name.TrimEnd('\0').Trim();
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public MonitorRect MonitorArea;
        public MonitorRect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public static MonitorInfoEx Create()
        {
            return new MonitorInfoEx
            {
                Size = (uint)Marshal.SizeOf<MonitorInfoEx>(),
                DeviceName = string.Empty
            };
        }
    }
}
