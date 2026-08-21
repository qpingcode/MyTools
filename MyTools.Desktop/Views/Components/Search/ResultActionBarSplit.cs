using MyTools.Common;

namespace MyTools.Desktop.Components;

public static class ResultActionBarSplit
{
    public static (IActionWithCommand? primary, IReadOnlyList<IActionWithCommand> overflow) Split(
        IEnumerable<IActionWithCommand>? actions)
    {
        var list = actions?.ToList() ?? [];
        if (list.Count == 0)
        {
            return (null, []);
        }

        var primary = list.FirstOrDefault(action =>
                          string.Equals(action.Command, Commands.DefaultCommand, StringComparison.Ordinal))
                      ?? list[0];
        var overflow = list.Where(action => !ReferenceEquals(action, primary)).ToList();
        return (primary, overflow);
    }
}
