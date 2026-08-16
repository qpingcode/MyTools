namespace MyTools.Desktop.Models;

/// <summary>
/// 用户配置的鼠标手势项，持久化到 Gestures.json。
/// </summary>
public sealed class GestureConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 手势方向序列，例如 ["Down", "Right"]。
    /// </summary>
    public List<string> Directions { get; set; } = new();

    /// <summary>
    /// 手势动作的显示名称，例如 "Close Tab"。
    /// </summary>
    public string ActionName { get; set; } = "";

    /// <summary>
    /// 动作类型："hotkey"（模拟快捷键）或 "mouse"（模拟鼠标按键）。
    /// </summary>
    public string ActionType { get; set; } = "hotkey";

    /// <summary>
    /// hotkey 类型下要模拟的快捷键，例如 "Ctrl+W"。
    /// </summary>
    public string? HotKey { get; set; }

    /// <summary>
    /// mouse 类型下要模拟的鼠标按键，例如 Left / Right / Middle / XButton1 / XButton2。
    /// </summary>
    public string? MouseButton { get; set; }

    /// <summary>
    /// 触发该手势的进程名列表（小写，无扩展名）。空列表表示对所有进程生效。
    /// </summary>
    public List<string> ProcessNames { get; set; } = new();

    public bool IsEnabled { get; set; } = true;
}
