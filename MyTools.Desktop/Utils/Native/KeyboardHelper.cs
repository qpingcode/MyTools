using System.Windows.Input;

namespace MyTools.Desktop.Utils;

public class KeyboardHelper
{
    private const int KeyPressDelay = 10;
    // https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
    private static Dictionary<Key, byte> KeyMap = new()
    {
        { Key.D0, 0x30 },
        { Key.D1, 0x31 },
        { Key.D2, 0x32 },
        { Key.D3, 0x33 },
        { Key.D4, 0x34 },
        { Key.D5, 0x35 },
        { Key.D6, 0x36 },
        { Key.D7, 0x37 },
        { Key.D8, 0x38 },
        { Key.D9, 0x39 },
        { Key.A, 0x41 },
        { Key.B, 0x42 },
        { Key.C, 0x43 },
        { Key.D, 0x44 },
        { Key.E, 0x45 },
        { Key.F, 0x46 },
        { Key.G, 0x47 },
        { Key.H, 0x48 },
        { Key.I, 0x49 },
        { Key.J, 0x4A },
        { Key.K, 0x4B },
        { Key.L, 0x4C },
        { Key.M, 0x4D },
        { Key.N, 0x4E },
        { Key.O, 0x4F },
        { Key.P, 0x50 },
        { Key.Q, 0x51 },
        { Key.R, 0x52 },
        { Key.S, 0x53 },
        { Key.T, 0x54 },
        { Key.U, 0x55 },
        { Key.V, 0x56 },
        { Key.W, 0x57 },
        { Key.X, 0x58 },
        { Key.Y, 0x59 },
        { Key.Z, 0x5A },
        { Key.F1, 0x70 },
        { Key.F2, 0x71 },
        { Key.F3, 0x72 },
        { Key.F4, 0x73 },
        { Key.F5, 0x74 },
        { Key.F6, 0x75 },
        { Key.F7, 0x76 },
        { Key.F8, 0x77 },
        { Key.F9, 0x78 },
        { Key.F10, 0x79 },
        { Key.F11, 0x7A },
        { Key.F12, 0x7B },
        { Key.Insert, 0x2D },
        { Key.Delete, 0x2E },
        { Key.Home, 0x24 },
        { Key.End, 0x23 },
        { Key.PageUp, 0x21 },
        { Key.PageDown, 0x22 },
        { Key.Up, 0x26 },
        { Key.Down, 0x28 },
        { Key.Left, 0x25 },
        { Key.Right, 0x27 },
        { Key.Space, 0x20 },
        { Key.Enter, 0x0D },
        { Key.Tab, 0x09 },
        { Key.Escape, 0x1B },
        { Key.Back, 0x08 }
    };
    
    public static void SimulateKeyPress(Key key)
    {
        PressKey(key);
        PressKey(key, true);
    }
    
    public static void SimulateKeyPress(ModifierKeys modifierKeys, Key key)
    {
        PressModifierKeys(modifierKeys);
        Thread.Sleep(KeyPressDelay);
        PressKey(key);
        Thread.Sleep(KeyPressDelay);
        PressKey(key, true);
        Thread.Sleep(KeyPressDelay);
        PressModifierKeys(modifierKeys, true);
    }

    private static void PressKey(Key key, bool isRelease = false)
    {
        var dwFlags = isRelease ? 0x0002 : 0;
        byte virtualKeyCode = KeyMap[key];
        Native.keybd_event(virtualKeyCode, 0, dwFlags, 0); // 按下
    }

    private static void PressModifierKeys(ModifierKeys modifierKeys, bool isRelease = false)
    {
        var dwFlags = isRelease ? 0x0002 : 0;
        if (modifierKeys.HasFlag(ModifierKeys.Control))
        {
            Native.keybd_event(0x11, 0, dwFlags, 0); // Ctrl
        }
        if (modifierKeys.HasFlag(ModifierKeys.Shift))
        {
            Native.keybd_event(0x10, 0, dwFlags, 0); // Shift
        }
        if (modifierKeys.HasFlag(ModifierKeys.Alt))
        {
            Native.keybd_event(0x12, 0, dwFlags, 0); // Alt
        }
        if (modifierKeys.HasFlag(ModifierKeys.Windows))
        {
            Native.keybd_event(0x5B, 0, dwFlags, 0); // Windows
        }
    }
}