using MyTools.Common;

namespace MyTools.Desktop.Components;

public static class ResultActionBarSplit
{
    public static (IActionWithHotkey? primary, IReadOnlyList<IActionWithHotkey> overflow) Split(
        IEnumerable<IActionWithHotkey>? actions)
    {
        var list = actions?.ToList() ?? [];
        if (list.Count == 0)
        {
            return (null, []);
        }

        var primary = list.FirstOrDefault(action => action.Hotkey == Hotkey.Enter)
                      ?? list[0];
        var overflow = list.Where(action => !ReferenceEquals(action, primary)).ToList();
        return (primary, overflow);
    }
}
