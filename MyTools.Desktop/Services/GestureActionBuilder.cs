using System.Windows.Input;
using MyTools.Desktop.Models;
using MyTools.Desktop.Utils;

namespace MyTools.Desktop.Services;

/// <summary>
/// 将 <see cref="GestureConfig"/> 转换为手势触发时执行的 <see cref="Action{T}"/>。
/// </summary>
public static class GestureActionBuilder
{
    public static Action<MouseGestureEventArgs> BuildAction(GestureConfig config, MouseHelper mouseHelper)
    {
        if (string.Equals(config.ActionType, "mouse", StringComparison.OrdinalIgnoreCase))
        {
            return BuildMouseAction(config, mouseHelper);
        }

        return BuildHotKeyAction(config);
    }

    private static Action<MouseGestureEventArgs> BuildHotKeyAction(GestureConfig config)
    {
        var hotKey = new HotKeyConfig(config.HotKey);
        var key = hotKey.Key;
        var modifiers = hotKey.Modifiers;

        if (key == Key.None)
        {
            return _ => { };
        }

        return modifiers == ModifierKeys.None
            ? _ => KeyboardHelper.SimulateKeyPress(key)
            : _ => KeyboardHelper.SimulateKeyPress(modifiers, key);
    }

    private static Action<MouseGestureEventArgs> BuildMouseAction(GestureConfig config, MouseHelper mouseHelper)
    {
        return config.MouseButton switch
        {
            "XButton1" => args => mouseHelper.XButton1Click(args.LastPoint),
            "XButton2" => args => mouseHelper.XButton2Click(args.LastPoint),
            _ => _ => { }
        };
    }

    /// <summary>
    /// 将字符串方向列表转换为 <see cref="MoveDirection"/> 数组。
    /// </summary>
    public static MoveDirection[] ToMoveDirections(List<string> directions)
    {
        var result = new MoveDirection[directions.Count];
        for (var i = 0; i < directions.Count; i++)
        {
            result[i] = ToMoveDirection(directions[i]);
        }
        return result;
    }

    private static MoveDirection ToMoveDirection(string value)
    {
        return value switch
        {
            "Up" => MoveDirection.Up,
            "Down" => MoveDirection.Down,
            "Left" => MoveDirection.Left,
            "Right" => MoveDirection.Right,
            _ => throw new ArgumentException($"Unknown direction: {value}")
        };
    }
}
