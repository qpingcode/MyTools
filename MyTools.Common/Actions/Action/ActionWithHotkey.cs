namespace MyTools.Common;

public class ActionWithHotkey : IActionWithHotkey
{
    private readonly IAction inner;

    public ActionWithHotkey(IAction action, Hotkey hotkey)
    {
        inner = action;
        Hotkey = hotkey;
    }

    public Hotkey Hotkey { get; }

    public string Name => inner.Name;

    public string Description => inner.Description;

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        return inner.ExecuteAsync(args);
    }
}
