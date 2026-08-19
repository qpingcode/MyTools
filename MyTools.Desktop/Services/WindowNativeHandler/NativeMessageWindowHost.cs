using System.Windows.Interop;
using MyTools.Common;
using MyTools.Common.WindowsMessageHandler;
using MyTools.Desktop.Utils;

namespace MyTools.Desktop.Services.WindowNativeHandler;

public class NativeMessageWindowHost : IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);
    private IntPtr _messageOnlyWindowHandle;
    private readonly IGlobal _global;
    private readonly IEnumerable<IWindowMessageHandler> _messageHandlers;
    private readonly IEnumerable<IWindowHandleAware> _messageWindowHandleAwares;
    private HwndSource? _source;
    private bool _clipboardListenerAttached;
    private bool _disposed;

    public NativeMessageWindowHost(
        IGlobal global,
        IEnumerable<IWindowMessageHandler> messageHandlers,
        IEnumerable<IWindowHandleAware> messageWindowHandleAwares)
    {
        _global = global;
        _messageHandlers = messageHandlers.OrderByDescending(h => h.Priority);
        _messageWindowHandleAwares = messageWindowHandleAwares;
    }

    public void EnsureCreated()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("MyTools.NativeMessageWindowHost")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(parameters);
        _messageOnlyWindowHandle = _source.Handle;

        _source.AddHook(WndProc);
        if (HasClipboardMessageHandler())
        {
            MessageNative.AddClipboardFormatListener(_messageOnlyWindowHandle);
            _clipboardListenerAttached = true;
        }

        foreach (var handle in _messageWindowHandleAwares)
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
        if (_disposed)
        {
            handled = false;
            return IntPtr.Zero;
        }

        _global.PreviousFocusHwd = Native.GetForegroundWindow();

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

    public void Dispose()
    {
        _disposed = true;
        if (_source is null)
        {
            return;
        }

        if (_clipboardListenerAttached)
        {
            MessageNative.RemoveClipboardFormatListener(_messageOnlyWindowHandle);
            _clipboardListenerAttached = false;
        }

        _source.Dispose();
        _source = null;
        _messageOnlyWindowHandle = IntPtr.Zero;
    }
}