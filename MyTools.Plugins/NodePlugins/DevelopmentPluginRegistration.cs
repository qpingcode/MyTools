using System.IO;
using System.Text.Json;
using MyTools.Common.Config;

namespace MyTools.Plugins.NodePlugins;

public sealed record DevelopmentPluginRegistration(
    string PluginId,
    string Name,
    string Author,
    string PluginType,
    string SourcePath,
    string DistPath);

public static class DevelopmentPluginRegistrationStore
{
    public static string FilePath => Path.Combine(ConfigPath.Base, "development-plugins.json");

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
        Directory.CreateDirectory(ConfigPath.Base);
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

    public static void Clear()
    {
        lock (Sync) ActivePluginIds.Clear();
    }
}
