using MyTools.Common;

namespace MyTools.Plugins.Param;

public class ActionParamT<T> : IActionParams
{
    public ActionParamT(T value)
    {
        this.value = value;
    }
    
    public T GetValue()
    {
        return value;
    }
    
    private T value;
}
