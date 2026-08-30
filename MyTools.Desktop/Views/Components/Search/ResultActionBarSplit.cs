using MyTools.Common;

namespace MyTools.Desktop.Components;

public static class ResultActionBarSplit
{
    public static (IReadOnlyList<IActionWithHotkey> primary, IReadOnlyList<IActionWithHotkey> overflow) Split(
        IEnumerable<IActionWithHotkey>? actions)
    {
        var list = actions?.ToList() ?? [];
        if (list.Count == 0)
        {
            return ([], []);
        }

        var pinned = list.Where(action => action.Pinned).ToList();
        if (pinned.Count > 0)
        {
            return (pinned, list.Where(action => !action.Pinned).ToList());
        }

        var primary = list.FirstOrDefault(action => action.Hotkey == Hotkey.Enter)
                      ?? list[0];
        var overflow = list.Where(action => !ReferenceEquals(action, primary)).ToList();
        return ([primary], overflow);
    }
}
