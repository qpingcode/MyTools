using System.Windows.Input;
using MyTools.Common;

namespace MyTools.Desktop.Components;

internal static class ResultActionBarHotkeys
{
    public static bool IsCtrlKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl;

    public static bool IsOverflowToggle(Key key, Key systemKey, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control)
        {
            return false;
        }

        return key == Key.K || systemKey == Key.K;
    }

    public static Hotkey? ToHotkey(Key key, ModifierKeys modifiers)
    {
        var hotkeyKey = ToHotkeyKey(key);
        if (hotkeyKey == HotkeyKey.None)
        {
            return null;
        }

        var hotkeyModifiers = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) hotkeyModifiers |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) hotkeyModifiers |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) hotkeyModifiers |= HotkeyModifiers.Shift;
        if ((modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0)
        {
            return null;
        }

        return new Hotkey(hotkeyKey, hotkeyModifiers);
    }

    public static HotkeyKey ToHotkeyKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (HotkeyKey)((int)HotkeyKey.A + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (HotkeyKey)((int)HotkeyKey.D0 + (key - Key.D0)),
        >= Key.F1 and <= Key.F12 => (HotkeyKey)((int)HotkeyKey.F1 + (key - Key.F1)),
        Key.Enter or Key.Return => HotkeyKey.Enter,
        Key.Tab => HotkeyKey.Tab,
        Key.Space => HotkeyKey.Space,
        Key.Delete => HotkeyKey.Delete,
        Key.Back => HotkeyKey.Backspace,
        Key.Escape => HotkeyKey.Escape,
        Key.Left => HotkeyKey.Left,
        Key.Right => HotkeyKey.Right,
        Key.Up => HotkeyKey.Up,
        Key.Down => HotkeyKey.Down,
        _ => HotkeyKey.None
    };
}
