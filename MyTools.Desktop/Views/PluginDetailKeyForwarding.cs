using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MyTools.Desktop.Views;

/// <summary>
/// 决定宿主窗口的哪些按键要交给插件详情页。焦点已经在网页里时页面自己就能收到按键，
/// 宿主不再转发，避免同一次按键被处理两遍。
/// </summary>
internal static class PluginDetailKeyForwarding
{
    public const string Enter = "Enter";
    public const string ShiftEnter = "Shift+Enter";

    /// <summary>
    /// 转发给页面的按键名，<c>null</c> 表示这个按键不该转发。
    /// </summary>
    public static string? ResolveHostKey(Key key, ModifierKeys modifiers)
    {
        if (key != Key.Enter)
        {
            return null;
        }

        return modifiers switch
        {
            ModifierKeys.None => Enter,
            ModifierKeys.Shift => ShiftEnter,
            _ => null
        };
    }

    /// <summary>Tab 不转发给页面，只用来把键盘焦点移进网页。</summary>
    public static bool IsFocusIntoPageKey(Key key, ModifierKeys modifiers) =>
        key == Key.Tab && modifiers == ModifierKeys.None;

    /// <summary>
    /// 按钮拥有焦点时 Enter 属于按钮自己的激活键；网页已获得焦点时按键会直达页面。
    /// 这两种情况都不该由宿主再转发一次。
    /// </summary>
    public static bool CanForward(IInputElement? focusedElement, bool isPluginContentFocused) =>
        !isPluginContentFocused && focusedElement is not ButtonBase;
}
