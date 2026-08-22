namespace MyTools.Common;

public interface IActionWithHotkey : IAction
{
    /// <summary>按键绑定；<see cref="Hotkey.None"/> 表示只能从 action bar 点击触发。</summary>
    Hotkey Hotkey { get; }
}
