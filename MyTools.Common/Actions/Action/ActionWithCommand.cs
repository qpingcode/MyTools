namespace MyTools.Common;

public class ActionWithCommand : IActionWithCommand
{
    private readonly IAction inner;
    private readonly string command;

    public ActionWithCommand(IAction action, string command)
    {
        inner = action;
        this.command = command;
    }

    public string Command => command;

    public string Name => inner.Name;

    public string Description => inner.Description;

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        return inner.ExecuteAsync(args);
    }
}