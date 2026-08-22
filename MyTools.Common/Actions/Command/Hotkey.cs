namespace MyTools.Common;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4
}

/// <summary>
/// 可以绑定到 action 的按键。刻意只开放这一组，避免把任意字符串当快捷键。
/// 数值顺序被 <c>A..Z</c>/<c>D0..D9</c>/<c>F1..F12</c> 的区间偏移计算依赖，不要重排。
/// </summary>
public enum HotkeyKey
{
    None = 0,

    Enter,
    Tab,
    Space,
    Delete,
    Backspace,
    Escape,
    Left,
    Right,
    Up,
    Down,

    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
}

/// <summary>
/// 一个 action 的按键绑定。<see cref="None"/> 表示"只能点击，没有快捷键"——
/// 它不再兼任动作身份，同一组 action 里可以有多个 <see cref="None"/>。
/// </summary>
public readonly record struct Hotkey(HotkeyKey Key, HotkeyModifiers Modifiers = HotkeyModifiers.None)
{
    public static readonly Hotkey None = default;

    /// <summary>默认动作的绑定：回车。</summary>
    public static readonly Hotkey Enter = new(HotkeyKey.Enter);

    public static Hotkey Ctrl(HotkeyKey key) => new(key, HotkeyModifiers.Control);

    public static Hotkey CtrlShift(HotkeyKey key) =>
        new(key, HotkeyModifiers.Control | HotkeyModifiers.Shift);

    public bool IsAssigned => Key != HotkeyKey.None;

    public static bool TryParse(string? key, int modifiers, out Hotkey hotkey)
    {
        hotkey = None;
        if (!Enum.TryParse<HotkeyKey>(key, ignoreCase: true, out var parsedKey)
            || parsedKey == HotkeyKey.None
            || modifiers < 0
            || (modifiers & ~(int)(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift)) != 0)
        {
            return false;
        }

        hotkey = new Hotkey(parsedKey, (HotkeyModifiers)modifiers);
        return true;
    }

    /// <summary>渲染顺序固定为 Ctrl → Alt → Shift → 主键。</summary>
    public IReadOnlyList<string> ToTokens()
    {
        if (!IsAssigned)
        {
            return [];
        }

        var tokens = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) tokens.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) tokens.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) tokens.Add("Shift");
        tokens.Add(DisplayKey(Key));
        return tokens;
    }

    public override string ToString() => IsAssigned ? string.Join("+", ToTokens()) : string.Empty;

    private static string DisplayKey(HotkeyKey key) => key switch
    {
        >= HotkeyKey.D0 and <= HotkeyKey.D9 => ((char)('0' + (key - HotkeyKey.D0))).ToString(),
        HotkeyKey.Backspace => "Backspace",
        _ => key.ToString()
    };
}
