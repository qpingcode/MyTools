namespace MyTools.Common;

public interface IActionWithCommand : IAction
{
    string Command { get; }
}