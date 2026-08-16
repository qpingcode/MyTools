namespace MyTools.Desktop.Models;

public interface IAppConfig
{
    public string SearchHotKeyText { get; set; }
    public HotKeyConfig SearchHotKey { get; }
    public string Language { get; set; }
    public string Theme { get; set; }
    public bool EnableClipboardHistory { get; set; }
}