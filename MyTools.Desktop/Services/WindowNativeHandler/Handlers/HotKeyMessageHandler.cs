using System.Runtime.InteropServices;
using System.Windows.Input;
using MyTools.Common;
using MyTools.Common.WindowsMessageHandler;

namespace MyTools.Desktop.Services;

public class HotKeyMessageHandler: IWindowMessageHandler, IWindowHandleAware
{
    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly Dictionary<int, (Key key, ModifierKeys modifiers, Action callback)> _registrations = new();
    private IntPtr _messageWindowHandle;
    private static int _currentId;
    private bool _suspended;

    public bool IsSuspended => _suspended;

    public IEnumerable<WindowsMessageType> Messages => [WindowsMessageType.HotKey];

    public void Handle(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        int id = wParam.ToInt32();
        if (_callbacks.ContainsKey(id))
        {
            _callbacks[id].Invoke();
            handled = true;
        }
    }

    public void initializeWindowHandle(IntPtr hwnd)
    {
        _messageWindowHandle = hwnd;
    }

    public void UnregisterCallback(int id)
    {
        if (!_callbacks.ContainsKey(id))
            return;

        HotKeyNativeMethods.UnregisterHotKey(_messageWindowHandle, id);
        _callbacks.Remove(id);
        _registrations.Remove(id);
    }

    public void UnregisterAllCallback()
    {
        foreach (int id in _callbacks.Keys)
        {
            HotKeyNativeMethods.UnregisterHotKey(_messageWindowHandle, id);
        }
        _callbacks.Clear();
        _registrations.Clear();
    }

    /// <summary>
    /// 暂停所有全局热键（取消 Win32 注册），但保留内部注册信息。
    /// 用于热键录制期间避免系统拦截按键。调用 <see cref="ResumeAll"/> 恢复。
    /// </summary>
    public void SuspendAll()
    {
        if (_suspended)
            return;

        foreach (int id in _callbacks.Keys)
        {
            HotKeyNativeMethods.UnregisterHotKey(_messageWindowHandle, id);
        }
        _callbacks.Clear();
        _suspended = true;
    }

    /// <summary>
    /// 恢复所有之前注册的热键（重新 Win32 注册）。
    /// </summary>
    public void ResumeAll()
    {
        if (!_suspended)
            return;

        _suspended = false;
        foreach (var (id, (key, modifiers, callback)) in _registrations)
        {
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            var modifierKey = GetNativeModifiers(modifiers);
            HotKeyNativeMethods.RegisterHotKey(_messageWindowHandle, id, modifierKey, vk);
            _callbacks[id] = callback;
        }
    }

    public int Register(Key key, ModifierKeys modifiers, Action callback)
    {
        _currentId++;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var modifierKey = GetNativeModifiers(modifiers);
        bool result = HotKeyNativeMethods.RegisterHotKey(_messageWindowHandle, _currentId, modifierKey, vk);

        if (!result)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to register hotkey: {key} with modifiers: {modifiers}, error code: {error}");
        }

        _callbacks[_currentId] = callback;
        _registrations[_currentId] = (key, modifiers, callback);
        return _currentId;
    }
    
    private static uint GetNativeModifiers(ModifierKeys modifiers)
    {
        uint modifierKey = 0;
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            modifierKey |= 0x0001; // MOD_ALT
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            modifierKey |= 0x0002; // MOD_CONTROL
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            modifierKey |= 0x0004; // MOD_SHIFT
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
            modifierKey |= 0x0008; // MOD_WIN
        return modifierKey;
    }

    internal static class HotKeyNativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
    
    int IWindowMessageHandler.Priority => IWindowMessageHandler.DefaultPriority;
}