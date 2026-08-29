using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.Plugins;

namespace MyTools.Desktop.Services;

/// <summary>
/// 用户对插件热键/关键词/启用状态的覆盖配置。
/// 存储在 %AppData%/MyTools.Desktop/PluginOverrides.json 中，优先于 plugin.json 中的默认值。
/// </summary>
public sealed class PluginOverrideProvider
{
    private static readonly string FilePath = Path.Combine(ConfigPath.Base, "PluginOverrides.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<PluginOverrideProvider> logger;
    private Dictionary<string, PluginOverride> overrides = new(StringComparer.OrdinalIgnoreCase);

    public PluginOverrideProvider(ILogger<PluginOverrideProvider> logger)
    {
        this.logger = logger;
        Load();
    }

    public string? GetHotKey(string overrideKey, PluginId? legacyPluginId = null)
    {
        return GetOverride(overrideKey, legacyPluginId)?.HotKey;
    }

    public List<string>? GetKeywords(string overrideKey, PluginId? legacyPluginId = null)
    {
        return GetOverride(overrideKey, legacyPluginId)?.Keywords;
    }

    public bool? GetIsEnabled(string overrideKey, PluginId? legacyPluginId = null)
    {
        return GetOverride(overrideKey, legacyPluginId)?.IsEnabled;
    }

    public bool? GetIncludeInGlobalResults(string overrideKey, PluginId? legacyPluginId = null)
    {
        return GetOverride(overrideKey, legacyPluginId)?.IncludeInGlobalResults;
    }

    public IReadOnlyDictionary<string, PluginOverride> GetAll()
    {
        return overrides;
    }

    public void Save(Dictionary<string, PluginOverride> newOverrides)
    {
        overrides = new Dictionary<string, PluginOverride>(newOverrides, StringComparer.OrdinalIgnoreCase);
        Persist();
    }

    private PluginOverride? GetOverride(string overrideKey, PluginId? legacyPluginId)
    {
        if (overrides.TryGetValue(overrideKey, out var value))
        {
            return value;
        }

        return legacyPluginId is not null
               && overrides.TryGetValue(legacyPluginId.Value, out value)
            ? value
            : null;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                overrides = new Dictionary<string, PluginOverride>(StringComparer.OrdinalIgnoreCase);
                Persist();
                return;
            }

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, PluginOverride>>(json, JsonOptions)
                         ?? new Dictionary<string, PluginOverride>();
            overrides = new Dictionary<string, PluginOverride>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load PluginOverrides.json.");
            overrides = new Dictionary<string, PluginOverride>(StringComparer.OrdinalIgnoreCase);
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
            logger.LogError(ex, "Failed to save PluginOverrides.json.");
        }
    }
}

/// <summary>
/// 单个插件的覆盖项。所有字段为 null 表示用 plugin.json 默认值。
/// </summary>
public sealed class PluginOverride
{
    public string? HotKey { get; set; }
    public List<string>? Keywords { get; set; }
    public bool? IsEnabled { get; set; }
    /// <summary>Override for whether the plugin appears in unscoped search results.</summary>
    public bool? IncludeInGlobalResults { get; set; }
}
