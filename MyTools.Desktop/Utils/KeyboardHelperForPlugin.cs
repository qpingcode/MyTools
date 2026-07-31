using System.Windows.Input;
using MyTools.Common.Utils;

namespace MyTools.Desktop.Utils;

public class KeyboardHelperForPlugin : IKeyboardHelper
{
    public void Paste()
    {
        KeyboardHelper.SimulateKeyPress(ModifierKeys.Control, Key.V);
    }
}