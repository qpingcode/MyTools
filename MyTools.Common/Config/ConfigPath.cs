namespace MyTools.Common.Config;

public static class ConfigPath
{
    public static readonly string Base = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyTools.Desktop");
    
    public static readonly string DatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyTools.Desktop", "Database");

    public static readonly string WebView2UserDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyTools.Desktop", "WebView2");
}