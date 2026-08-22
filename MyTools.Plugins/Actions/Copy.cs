using System.Windows;
using MyTools.Common;
using MyTools.Common.Localization;
using System.Runtime.InteropServices;

namespace MyTools.Plugins;

public class Copy : IAction
{
    public virtual string Name => ActionText.Get("Action.Copy.Name", "Copy");
    public virtual string Description => ActionText.Get("Action.Copy.Description", "Copy the selected text to clipboard");
    public virtual Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        try
        {
            if (Copied(args))
            {
                var result = ActionResult.CreateSuccess(new LocalizedMessage(
                    "Action.Copy.Success", "Copied to clipboard"));
                return Task.FromResult(result);
            }

            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.Copy.InvalidParameters", "Invalid parameters for Copy action")));
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x800401D0))
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.Copy.ClipboardBusy", "The clipboard is busy. Please try again.")));
        }
    }

    protected bool Copied(IActionParams args)
    {
        if (args is IClipboardSource clipboardSource)
        {
            var dataForClipboard = clipboardSource.GetDataForClipboard();
            ClipboardAccess.Execute(() => Clipboard.SetDataObject(dataForClipboard, true));
            return true;
        } 
        
        if (args is IActionStringParam stringParam)
        {
            ClipboardAccess.Execute(() => Clipboard.SetText(stringParam.GetValue()));
            return true;
        }
        
        return false;
    }
}
