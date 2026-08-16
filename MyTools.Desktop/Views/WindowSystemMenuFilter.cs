using System;

namespace MyTools.Desktop.Views;

/// <summary>
/// Win32 system-menu shortcuts (notably Alt+Space) that must be swallowed while a
/// hotkey capture dialog is open, otherwise Windows shows the window menu instead
/// of letting the recorder see the chord.
/// </summary>
internal static class WindowSystemMenuFilter
{
    public const int WmSysCommand = 0x0112;
    public const int WmSysKeyDown = 0x0104;
    public const int ScKeyMenu = 0xF100;
    public const int VkSpace = 0x20;

    public static bool IsCandidate(int msg) =>
        msg == WmSysCommand || msg == WmSysKeyDown;

    public static bool ShouldSuppress(int msg, IntPtr wParam, bool capturing)
    {
        if (!capturing)
        {
            return false;
        }

        if (msg == WmSysKeyDown && (wParam.ToInt32() & 0xFFFF) == VkSpace)
        {
            return true;
        }

        if (msg == WmSysCommand && (wParam.ToInt32() & 0xFFF0) == ScKeyMenu)
        {
            return true;
        }

        return false;
    }
}
