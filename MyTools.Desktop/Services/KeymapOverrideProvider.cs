using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;

namespace MyTools.Desktop.Services;

/// <summary>
/// 用户对插件热键/关键词/启用状态的覆盖配置。
/// 存储在 %AppData%/MyTools.Desktop/Keymap.json 中，优先于 plugin.json 中的默认值。
/// </summary>
public sealed class KeymapOverrideProvider
{
    private static readonly string FilePath = Path.Combine(ConfigPath.Base, "Keymap.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<KeymapOverrideProvider> logger;
    private Dictionary<string, KeymapOverride> overrides = new();

    public KeymapOverrideProvider(ILogger<KeymapOverrideProvider> logger)
    {
        this.logger = logger;
        Load();
    }

    public string? GetHotKey(string pluginId)
    {
        return overrides.TryGetValue(pluginId, out var o) ? o.HotKey : null;
    }

    public List<string>? GetKeywords(string pluginId)
    {
        return overrides.TryGetValue(pluginId, out var o) ? o.Keywords : null;
    }

    public bool? GetIsEnabled(string pluginId)
    {
        return overrides.TryGetValue(pluginId, out var o) ? o.IsEnabled : null;
    }

    public bool? GetIncludeInGlobalResults(string pluginId)
    {
        return overrides.TryGetValue(pluginId, out var o) ? o.IncludeInGlobalResults : null;
    }

    public IReadOnlyDictionary<string, KeymapOverride> GetAll()
    {
        return overrides;
    }

    public void Save(Dictionary<string, KeymapOverride> newOverrides)
    {
        overrides = newOverrides;
        Persist();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var json = File.ReadAllText(FilePath);
            overrides = JsonSerializer.Deserialize<Dictionary<string, KeymapOverride>>(json, JsonOptions)
                        ?? new Dictionary<string, KeymapOverride>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load Keymap.json.");
            overrides = new Dictionary<string, KeymapOverride>();
        }
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(ConfigPath.Base);
            var json = JsonSerializer.Serialize(overrides, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save Keymap.json.");
        }
    }
}

/// <summary>
/// 单个插件的覆盖项。所有字段为 null 表示用 plugin.json 默认值。
/// </summary>
public sealed class KeymapOverride
{
    public string? HotKey { get; set; }
    public List<string>? Keywords { get; set; }
    public bool? IsEnabled { get; set; }
    /// <summary>Override for whether the plugin appears in unscoped search results.</summary>
    public bool? IncludeInGlobalResults { get; set; }
}
