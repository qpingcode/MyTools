using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace MyTools.Desktop.Utils;

internal static class WindowForeground
{
    public static bool TryActivate(Window window)
    {
        return TryActivate(window, sendInput: false, logger: null, trigger: "direct");
    }

    public static bool TryActivateFromHotKey(Window window, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return TryActivate(window, sendInput: true, logger, trigger: "WM_HOTKEY");
    }

    private static bool TryActivate(Window window, bool sendInput, ILogger? logger, string trigger)
    {
        ArgumentNullException.ThrowIfNull(window);

        var previousHandle = Native.GetForegroundWindow();
        uint previousProcessId = 0;
        var previousThreadId = previousHandle == IntPtr.Zero
            ? 0
            : Native.GetWindowThreadProcessId(previousHandle, out previousProcessId);

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        if (!window.IsVisible)
        {
            window.Show();
        }

        var handle = new WindowInteropHelper(window).EnsureHandle();
        uint sentInputCount = 0;
        var sendInputError = 0;
        if (sendInput)
        {
            sentInputCount = Native.SendEmptyMouseInput();
            if (sentInputCount != 1)
            {
                sendInputError = Marshal.GetLastWin32Error();
            }
        }

        var setForegroundResult = Native.SetForegroundWindow(handle);
        var wpfActivateResult = window.Activate();
        _ = window.Focus();
        var finalHandle = Native.GetForegroundWindow();
        var activated = finalHandle == handle;
        if (!activated)
        {
            _ = Native.FlashTaskbar(handle);
        }

        LogActivation(
            logger,
            activated,
            trigger,
            window.GetType().Name,
            handle,
            previousHandle,
            previousThreadId,
            previousProcessId,
            sentInputCount,
            sendInputError,
            setForegroundResult,
            wpfActivateResult,
            finalHandle);
        return activated;
    }

    private static void LogActivation(
        ILogger? logger,
        bool activated,
        string trigger,
        string windowType,
        IntPtr targetHandle,
        IntPtr previousHandle,
        uint previousThreadId,
        uint previousProcessId,
        uint sentInputCount,
        int sendInputError,
        bool setForegroundResult,
        bool wpfActivateResult,
        IntPtr finalHandle)
    {
        if (logger == null)
        {
            return;
        }

        const string message =
            "Window foreground activation success={Success} trigger={Trigger} window={WindowType} target=0x{TargetHandle:X} " +
            "previous=0x{PreviousHandle:X} previousPid={PreviousProcessId} previousThread={PreviousThreadId} " +
            "currentPid={CurrentProcessId} currentIntegrity={CurrentIntegrity} previousIntegrity={PreviousIntegrity} " +
            "sendInputCount={SendInputCount} sendInputError={SendInputError} setForeground={SetForegroundResult} " +
            "wpfActivate={WpfActivateResult} final=0x{FinalHandle:X}";
        var values = new object?[]
        {
            activated,
            trigger,
            windowType,
            targetHandle.ToInt64(),
            previousHandle.ToInt64(),
            previousProcessId,
            previousThreadId,
            Environment.ProcessId,
            ProcessIntegrityLevel.Read((uint)Environment.ProcessId),
            ProcessIntegrityLevel.Read(previousProcessId),
            sentInputCount,
            sendInputError,
            setForegroundResult,
            wpfActivateResult,
            finalHandle.ToInt64()
        };

        if (activated)
        {
            logger.LogInformation(message, values);
        }
        else
        {
            logger.LogWarning(message, values);
        }
    }

    internal static bool TryActivateHandle(
        IntPtr handle,
        Func<IntPtr, bool> setForegroundWindow,
        Func<IntPtr> getForegroundWindow,
        Action<IntPtr> flashTaskbar)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        _ = setForegroundWindow(handle);
        var activated = getForegroundWindow() == handle;
        if (!activated)
        {
            flashTaskbar(handle);
        }
        return activated;
    }

    public static bool IsForeground(Window? window)
    {
        if (window == null || !window.IsVisible) return false;
        var handle = new WindowInteropHelper(window).Handle;
        // WPF IsActive can be true while another process owns the foreground.
        return handle != IntPtr.Zero && Native.GetForegroundWindow() == handle;
    }
}
