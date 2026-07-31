using MyTools.Common;

namespace MyTools.Plugins.Param;

public class ActionStringParam(string param) : IActionStringParam
{
    public string GetValue()
    {
        return param;
    }

    public static readonly IActionStringParam Empty = new ActionStringParam(string.Empty);
    
    public static IActionStringParam From(string param)
    {
        return new ActionStringParam(param);
    }
}