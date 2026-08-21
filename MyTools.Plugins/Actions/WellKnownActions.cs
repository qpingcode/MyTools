using MyTools.Common;

namespace MyTools.Plugins;

public static class WellKnownActions
{
    public static readonly IAction Execute = new Execute();
    public static readonly IAction AdminExecute = new AdminExecute();
    public static readonly IAction Copy = new Copy();
    public static readonly IAction OpenInExplorer = new OpenInExplorer();
    public static readonly IAction OpenInBrowser = new OpenInBrowser();
    public static readonly IAction CopyAndPaste = new CopyAndPaste();
    public static readonly IAction OpenPlugin = new OpenPluginWindowAction();
}