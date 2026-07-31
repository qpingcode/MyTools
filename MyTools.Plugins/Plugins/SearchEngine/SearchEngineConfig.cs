using System.Text.Json.Serialization;

namespace MyTools.Plugins;

public class SearchEngineConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; } = string.Empty;
    
    [JsonPropertyName("urls")]
    public List<string>? Urls { get; set; }

    [JsonPropertyName("shortcut")]
    public string? Shortcut { get; set; } = string.Empty;
} 