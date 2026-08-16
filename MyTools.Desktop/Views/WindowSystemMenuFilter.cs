using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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

    /// <summary>
    /// Alt+Space is delivered as <c>SC_KEYMENU</c>, not a Space keydown.
    /// Rebuild the chord from the current modifier state so the recorder can see it.
    /// </summary>
    public static string FormatSystemMenuChord()
    {
        var parts = new List<string>();
        if (IsDown(VkControl))
        {
            parts.Add("Ctrl");
        }

        if (IsDown(VkShift))
        {
            parts.Add("Shift");
        }

        parts.Add("Alt");

        if (IsDown(VkLWin) || IsDown(VkRWin))
        {
            parts.Add("Win");
        }

        parts.Add("Space");
        return string.Join("+", parts);
    }

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private static bool IsDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
