using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace MyTools.Desktop.Utils;

public class MouseHelper
{
    public const int SimulatedEventTag = 19900620;
    private const int INPUT_MOUSE = 0;
    private bool isSimulatingInput;

    public bool IsSimulatingInput => isSimulatingInput;

    public void RightClick(Point point)
    {
        ButtonClick(MouseButton.Right, point: point);
    }
    
    public void XButton1Click(Point point)
    {
        ButtonClick(MouseButton.XButton1, point: point );
    }

    public void XButton2Click(Point point)
    {
        ButtonClick(MouseButton.XButton2, point: point );
    }

    private void ButtonClick(MouseButton button, Point point)
    {
        isSimulatingInput = true;
        try
        {
            var inputs = CreateMouseInputs(button, point);
            var result = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
            if (result == 0)
            {
                throw new Win32Exception();
            }
        }
        finally
        {
            isSimulatingInput = false;
        }
    }

    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mouse_event
    private static Native.INPUT[] CreateMouseInputs(MouseButton button, Point point)
    {
        Native.INPUT[] inputs = new Native.INPUT[2];
            
        var x = (int) point.X;
        var y = (int) point.Y;
        uint mouseData = button switch
        {
            MouseButton.XButton1 => 0x0001,
            MouseButton.XButton2 => 0x0002,
            _ => (uint)0
        };
            
        uint mouseUpEvent = button switch
        {
            MouseButton.XButton1 => 0x0100,
            MouseButton.XButton2 => 0x0100,
            MouseButton.Left => 0x0004,
            MouseButton.Right => 0x0010,
            MouseButton.Middle => 0x0040,
            _ => (uint)0
        };
            
        uint mouseDownEvent = button switch
        {
            MouseButton.XButton1 => 0x0080,
            MouseButton.XButton2 => 0x0080,
            MouseButton.Left => 0x0002,
            MouseButton.Right => 0x0008,
            MouseButton.Middle => 0x0020,
            _ => (uint)0
        };
            
        inputs[0].type = INPUT_MOUSE;
        inputs[0].mi = new Native.MOUSEINPUT
        {
            dwFlags = mouseDownEvent,
            dx = x,
            dy = y,
            mouseData = mouseData,
            dwExtraInfo = SimulatedEventTag
        };
    
        inputs[1].type = INPUT_MOUSE;
        inputs[1].mi = new Native.MOUSEINPUT
        {
            dwFlags = mouseUpEvent,
            dx = x,
            dy = y,
            mouseData = mouseData,
            dwExtraInfo = SimulatedEventTag
        };
        return inputs;
    }
}
