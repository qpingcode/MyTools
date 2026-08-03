using System.Runtime.InteropServices;
using System.Windows;
using MyTools.Common;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Utils;

namespace MyTools.Plugins;

public sealed class CopyAndPaste : Copy
{
    public override string Name => ActionText.Get("Action.CopyAndPaste.Name", "Copy and Paste");
    public override string Description => ActionText.Get(
        "Action.CopyAndPaste.Description", "Copy and paste text to the previously focused window");
    
    public override async Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (Copied(args))
        {
            var global = ServiceLocator.GetRequiredService<IGlobal>();
            var keyboardHelper = ServiceLocator.GetRequiredService<IKeyboardHelper>();
            var previousFocusedWindow = global.PreviousFocusHwd;

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (IsIconic(previousFocusedWindow))
                {
                    ShowWindow(previousFocusedWindow, SW_RESTORE);  
                }
                SetForegroundWindow(previousFocusedWindow);
                await Task.Delay(80);
                keyboardHelper.Paste();
            });
            
            var result = ActionResult.CreateSuccess(new LocalizedMessage(
                "Action.CopyAndPaste.Success", "Copied to clipboard and pasted to the previously focused window."));
            return result;
        }
        
        return ActionResult.CreateFailure(new LocalizedMessage(
            "Action.CopyAndPaste.InvalidParameters", "Invalid parameters for Copy and Paste action"));
    }
    
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    
    
    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    const int SW_RESTORE = 9;
}