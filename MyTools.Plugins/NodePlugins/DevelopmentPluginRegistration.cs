using System.IO;
using System.Text.Json;
using MyTools.Common.Config;

namespace MyTools.Plugins.NodePlugins;

public sealed record DevelopmentPluginRegistration(
    string PluginId,
    string Name,
    string PluginType,
    string SourcePath,
    string DistPath)
{
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public IReadOnlyList<string> HotKeys { get; init; } = [];
    public IReadOnlyList<string> TestSteps { get; init; } = [];
    public bool IsDebugging { get; init; }
}

public static class DevelopmentPluginRegistrationStore
{
    public const string OwnerPluginId = "create-plugin";

    public static string DataDirectory => ConfigPath.PluginDataDirectory(OwnerPluginId);

    public static string FilePath => Path.Combine(DataDirectory, "development-plugins.json");

    public static IReadOnlyList<DevelopmentPluginRegistration> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<DevelopmentPluginRegistration>>(
                       File.ReadAllText(FilePath), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<DevelopmentPluginRegistration> registrations)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(registrations, JsonOptions));
        File.Move(temporaryPath, FilePath, true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

/// <summary>
/// Tracks development plugins activated by a watcher in the current MyTools process.
/// Persisted registrations describe projects, but do not make them load at startup.
/// </summary>
public static class DevelopmentPluginSession
{
    private static readonly HashSet<string> ActivePluginIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Sync = new();

    public static void Activate(string pluginId)
    {
        lock (Sync) ActivePluginIds.Add(pluginId);
    }

    public static bool IsActive(string pluginId)
    {
        lock (Sync) return ActivePluginIds.Contains(pluginId);
    }

    public static void Deactivate(string pluginId)
    {
        lock (Sync) ActivePluginIds.Remove(pluginId);
    }

    public static void Clear()
    {
        lock (Sync) ActivePluginIds.Clear();
    }
}
