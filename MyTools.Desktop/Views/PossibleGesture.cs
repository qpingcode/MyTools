using MyTools.Desktop.Utils;

namespace MyTools.Desktop.Views;

/// <summary>
/// 表示一个可能的手势
/// </summary>
public class PossibleGesture
{
    public MoveDirection[] Gesture { get; set; } = Array.Empty<MoveDirection>();
    public string ActionName { get; set; } = string.Empty;
    public int MatchedLength { get; set; }
}

