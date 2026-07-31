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

    public string Name {
        get {
            if (command == null) {
                return inner.Name;
            }
            return inner.Name + " (" + GetCommandLabel(command) + ")";
        }
    }

    private string GetCommandLabel(string command)
    {
        switch (command)
        {
            case "Ctrl+Enter":
                return "Ctrl+↩️";
            case "Enter":
                return "↩️";
            default:
                return command;
        }
    }

    public string Description => inner.Description;

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        return inner.ExecuteAsync(args);
    }
}