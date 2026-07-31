namespace MyTools.Plugins;

public class PluginConstants
{
    public const string KillProcessKeyword = "kill";
    public const string ClipboardHistory = "cb";
    public const string JsonFormatterKeyword = "json";
    public const string XmlFormatterKeyword = "xml";
    public const string UuidGeneratorKeyword = "guid";
    public static string DllInterfaceReaderKeyword = "dll";

    public const string PluginCachePrefix = "PluginCache_";
    public const string FileSearcherCachePrefix = "FileSearcher_";
}

public static class ResultItemPriorities
{
    public const int Low = 0;
    public const int Medium = 100;
    public const int High = 10_000;
    public const int Highest = int.MaxValue;
}