using MyTools.Common.WindowsMessageHandler;

namespace MyTools.Common;

public interface IWindowMessageHandler
{
    // Usually, the priority should be Default, which is 0.
    // The priority for the clipboard listener may need to be read from the configuration,
    // allowing users to decide which plugin should take priority.
    public const int DefaultPriority = 0;
    public const int HighPriority = 1000;
    public const int LowPriority = -1000;
    
    /// <summary>
    /// The types of Windows messages that this handler can handle
    /// </summary>
    IEnumerable<WindowsMessageType> Messages { get; }
    
    /// <summary>
    /// Handle Windows Messages
    /// </summary>
    void Handle(int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

    int Priority { get; }
}