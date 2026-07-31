using MyTools.Plugins;

namespace MyTools.Common;

public static class ActionExtensions
{
    public static IActionWithCommand WithCommand(this IAction action, string command)
    {
        return new ActionWithCommand(action, command);
    }
    
    public static IActionWithCommand WithDefaultCommand(this IAction action)
    {
        return new ActionWithCommand(action, Commands.DefaultCommand);
    }
}