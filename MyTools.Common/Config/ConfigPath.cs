namespace MyTools.Common.Config;

public static class ConfigPath
{
    public const string PluginSettingsFileName = "settings.json";

    public static readonly string Base = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyTools.Desktop");

    public static readonly string PluginsDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyTools.Desktop", "pluginsData");
    
    public static readonly string DatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyTools.Desktop", "Database");

    public static readonly string WebView2UserDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyTools.Desktop", "WebView2");

    public static string PluginDataDirectory(string pluginId) =>
        PluginDataDirectory(PluginsDataPath, pluginId);

    public static string PluginDataDirectory(string pluginsDataRoot, string pluginId) =>
        Path.Combine(pluginsDataRoot, SanitizePluginId(pluginId));

    public static string PluginSettingsPath(string pluginId) =>
        PluginSettingsPath(PluginsDataPath, pluginId);

    public static string PluginSettingsPath(string pluginsDataRoot, string pluginId) =>
        Path.Combine(PluginDataDirectory(pluginsDataRoot, pluginId), PluginSettingsFileName);

    public static string SanitizePluginId(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return "_plugin";
        }

        var sanitized = pluginId.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return sanitized;
    }
}
