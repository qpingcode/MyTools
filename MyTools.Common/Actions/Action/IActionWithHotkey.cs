namespace MyTools.Common;

public interface IActionWithHotkey : IAction
{
    /// <summary>按键绑定；<see cref="Hotkey.None"/> 表示只能从 action bar 点击触发。</summary>
    Hotkey Hotkey { get; }

    /// <summary>
    /// 为 true 时固定显示在 action bar 上，而不是收进溢出的 Actions 菜单。
    /// 有任意 pinned action 时，未 pinned 的都进溢出菜单；一个都没有时仍只显示默认项。
    /// </summary>
    bool Pinned => false;
}
