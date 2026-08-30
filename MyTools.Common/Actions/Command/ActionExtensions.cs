namespace MyTools.Common;

public static class ActionExtensions
{
    public static IActionWithHotkey WithHotkey(this IAction action, Hotkey hotkey)
    {
        return new ActionWithHotkey(action, hotkey);
    }

    public static IActionWithHotkey WithHotkey(this IAction action, HotkeyKey key, HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        return new ActionWithHotkey(action, new Hotkey(key, modifiers));
    }

    /// <summary>绑定回车，即该结果项的默认动作。</summary>
    public static IActionWithHotkey WithDefaultHotkey(this IAction action)
    {
        return new ActionWithHotkey(action, Hotkey.Enter);
    }

    /// <summary>只出现在 action bar 里，没有快捷键。</summary>
    public static IActionWithHotkey WithoutHotkey(this IAction action)
    {
        return new ActionWithHotkey(action, Hotkey.None);
    }

    /// <summary>固定显示在 action bar 上，不收进溢出的 Actions 菜单。</summary>
    public static IActionWithHotkey WithPinned(this IActionWithHotkey action, bool pinned = true)
    {
        return new ActionWithHotkey(action, action.Hotkey, pinned);
    }
}
