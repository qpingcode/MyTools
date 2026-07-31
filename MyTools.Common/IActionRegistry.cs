namespace MyTools.Common;

public interface IActionRegistry
{
    void Register(string hotkey, IAction action);
    IAction? GetAction(string hotkey, IEnumerable<IAction> allowedActions);
    string? GetHotKey(IAction action);
}