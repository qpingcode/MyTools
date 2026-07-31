namespace MyTools.Desktop.Models;

public interface IAppConfig
{
    public HotKeyConfig SearchHotKey { get; }
    public string Language { get; set; }
    public bool EnableGesture { get; set; }
    public bool EnableClipboardHistory { get; set; }
}