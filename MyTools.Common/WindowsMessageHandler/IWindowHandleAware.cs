namespace MyTools.Common.WindowsMessageHandler;

public interface IWindowHandleAware
{
    /// <summary>
    /// After the message window initialization is complete, this function will be called to pass in the window handle
    /// </summary>
    void initializeWindowHandle(IntPtr hwnd);
}