using System.Windows;
using System.Windows.Interop;
using MyTools.Common;
using MyTools.Common.DependencyInjection;
using MyTools.Common.WindowsMessageHandler;
using MyTools.Desktop.Utils;

namespace MyTools.Desktop.Services.WindowNativeHandler;

public class MessageOnlyWindow : Window
{
    private IntPtr _messageOnlyWindowHandle;
    private readonly IEnumerable<IWindowMessageHandler> _messageHandlers;
    private readonly IEnumerable<IWindowHandleAware> _messageWindowHandleAwares;

    public MessageOnlyWindow(IEnumerable<IWindowMessageHandler> messageHandlers, IEnumerable<IWindowHandleAware> messageWindowHandleAwares)
    {
        WindowStyle = WindowStyle.None;
        Width = 0;
        Height = 0;
        Visibility = Visibility.Visible;
        
        _messageHandlers = messageHandlers.OrderByDescending(h => h.Priority);
        _messageWindowHandleAwares = messageWindowHandleAwares;
        
        SourceInitialized += OnSourceInitialized!;
    }
    
    private void OnSourceInitialized(object sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _messageOnlyWindowHandle = helper.Handle;

        HwndSource? source = HwndSource.FromHwnd(_messageOnlyWindowHandle);
        if (source == null) return;
        
        source.AddHook(WndProc);
        if(HasClipboardMessageHandler())
        {
            MessageNative.AddClipboardFormatListener(_messageOnlyWindowHandle);
        }
        
        foreach(var handle in _messageWindowHandleAwares)
        {
            handle.initializeWindowHandle(_messageOnlyWindowHandle);
        }
    }

    private bool HasClipboardMessageHandler()
    {
        return _messageHandlers.Any(handler => handler.Messages.Contains(WindowsMessageType.ClipboardUpdate));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var global = ServiceLocator.GetRequiredService<IGlobal>();
        global.PreviousFocusHwd = Native.GetForegroundWindow();
        
        var messageType = (WindowsMessageType)msg;
        foreach (var handler in _messageHandlers)
        {
            if (handler.Messages.Contains(messageType))
            {
                handler.Handle(msg, wParam, lParam, ref handled);
                if (handled)
                    break;
            }
        }
        return IntPtr.Zero;
    }
}