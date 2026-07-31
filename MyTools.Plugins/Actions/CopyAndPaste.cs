using System.Runtime.InteropServices;
using System.Windows;
using MyTools.Common;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Utils;

namespace MyTools.Plugins;

public sealed class CopyAndPaste : Copy
{
    public override string Name => "Copy and Paste";
    public override string Description => "Copy and Paste text to the previous focused window";
    
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
            
            var result = ActionResult.CreateSuccess("Copied to clipboard and pasted to the previous focused window.");
            return result;
        }
        
        return ActionResult.CreateFailure("Invalid parameters for CopyAndPaste action");
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