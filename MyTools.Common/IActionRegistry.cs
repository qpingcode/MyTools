namespace MyTools.Common;

public interface IActionRegistry
{
    void Register(Hotkey hotkey, IAction action);
    IAction? GetAction(Hotkey hotkey, IEnumerable<IAction> allowedActions);
    Hotkey? GetHotkey(IAction action);
}
