namespace MyTools.Common;

public interface IAction
{
    /// <summary>
    /// Stable identifier used to reconnect persisted result metadata with the current action.
    /// Implementations with multiple semantic actions of the same runtime type must override it.
    /// </summary>
    string Id => GetType().FullName ?? GetType().Name;

    public string Name { get; }
    public string Description { get; }
    Task<ActionResult> ExecuteAsync(IActionParams args);
}
