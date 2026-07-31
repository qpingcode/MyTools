using System.Text.Json.Serialization;

namespace MyTools.Plugins;

public class CommandConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public string Args { get; set; } = string.Empty;

    [JsonPropertyName("runAsAdmin")]
    public bool RunAsAdmin { get; set; }

    [JsonPropertyName("isBashScript")] 
    public bool IsBashScript { get; set; } = false;
    
    [JsonPropertyName("scripts")]
    public List<string>? Scripts { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }
} 