namespace MyTools.Common;

public class ActionWithHotkey : IActionWithHotkey
{
    private readonly IAction inner;

    public ActionWithHotkey(IAction action, Hotkey hotkey, bool pinned = false)
    {
        inner = action;
        Hotkey = hotkey;
        Pinned = pinned;
    }

    public Hotkey Hotkey { get; }

    public bool Pinned { get; }

    public string Name => inner.Name;

    public string Description => inner.Description;

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        return inner.ExecuteAsync(args);
    }
}
