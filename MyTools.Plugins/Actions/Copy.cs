using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using MyTools.Common;

namespace MyTools.Plugins;

public class Copy : IAction
{
    public virtual string Name => "Copy";
    public virtual string Description => "Copy the selected text to clipboard";
    public virtual Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (Copied(args))
        {
            var result = ActionResult.CreateSuccess("Copied to clipboard");
            return Task.FromResult(result);
        }
      
        return Task.FromResult(ActionResult.CreateFailure("Invalid parameters for Copy action"));
    }

    protected bool Copied(IActionParams args)
    {
        if (args is IClipboardSource clipboardSource)
        {
            var dataForClipboard = clipboardSource.GetDataForClipboard();
            Clipboard.SetDataObject(dataForClipboard, true);
            return true;
        } 
        
        if (args is IActionStringParam stringParam)
        {
            Clipboard.SetText(stringParam.GetValue());
            return true;
        }
        
        return false;
    }
}