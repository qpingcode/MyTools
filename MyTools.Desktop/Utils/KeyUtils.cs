using System.Windows.Input;

namespace MyTools.Desktop.Utils;

public class KeyUtils
{
    public static bool IsNumber(Key key)
    {
        return key is >= Key.D0 and <= Key.D9;
    }

    public static bool IsLetterOrEnter(Key key)
    {
        return key is >= Key.A and <= Key.Z or Key.Enter;
    }
    
    public static bool IsSystemCommlyUsedKey(Key key)
    {
        return key is Key.A or Key.C or Key.V or Key.X;
    }
}