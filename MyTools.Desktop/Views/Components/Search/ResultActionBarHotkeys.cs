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

    public static string? ToCommand(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control)
        {
            return null;
        }

        if (key == Key.Enter)
        {
            return Commands.Ctrl_Enter;
        }

        if (key is >= Key.A and <= Key.Z)
        {
            return $"Ctrl+{key}";
        }

        return null;
    }
}
