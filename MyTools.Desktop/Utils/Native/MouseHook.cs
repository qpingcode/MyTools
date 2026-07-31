using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace MyTools.Desktop.Utils;

public class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private Native.LowLevelMouseProc? _proc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly ILogger<MouseGestureDetector> _logger;
    
    public delegate void MouseHookEventHandler(MouseHookEventArgs e);
    public event MouseHookEventHandler? MouseHookEvent;
    
    public MouseHook(ILogger<MouseGestureDetector> logger)
    {
        _logger = logger;
    }
    
    public void StartListening()
    {
        _proc = HookCallback;

        if (_hookHandle == IntPtr.Zero)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            if (curModule?.ModuleName != null)
            {
                _hookHandle = Native.SetWindowsHookEx(WH_MOUSE_LL, _proc, Native.GetModuleHandle(curModule.ModuleName), 0);
            }

            if (_hookHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to install mouse hook. Error code: {Error}", error);
            }
            else
            {
                _logger.LogInformation("Mouse hook installed successfully");
            }
        }
    }
    
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
        
        var hookStruct = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT))!;
        var args = new MouseHookEventArgs((Native.MouseMsg)wParam, hookStruct.dwExtraInfo.ToInt64());
        
        try
        {
            MouseHookEvent?.Invoke(args);
        }
        catch(Exception e)
        {
            _logger.LogError(e, "Error in MouseHookEvent");
        }

        return args.Handled ?  new IntPtr(-1) : Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
    
   

    public class MouseHookEventArgs(Native.MouseMsg mouseMsg, long extraInfo) : EventArgs
    {
        public Native.MouseMsg Msg { get; } = mouseMsg;

        public bool Handled { get; set; }
        public long ExtraInfo { get; } = extraInfo;
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}