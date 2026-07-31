namespace MyTools.Common;

public interface IAction
{
    public string Name { get; }
    public string Description { get; }
    Task<ActionResult> ExecuteAsync(IActionParams args);
}