using System.Text.Json;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// Node 插件向宿主发起的能力请求（hostCall）。
/// </summary>
public sealed record HostCallRequest(
    string Method,
    JsonElement Params,
    string PluginId = "",
    string SessionId = "");

// ── getConfiguration 响应 DTO ──

public sealed class ConfigurationDto
{
    public List<CategoryDto> Categories { get; init; } = new();
    public List<OptionDto> SupportedLocales { get; init; } = new();
    public List<OptionDto> SupportedThemes { get; init; } = new();
    public List<OptionDto> SupportedUpdateChannels { get; init; } = new();
    public List<OptionDto> SupportedLogLevels { get; init; } = new();
}

public sealed class CategoryDto
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public bool IsSelectable { get; init; }
    public List<SettingDto> Settings { get; init; } = new();
}

public sealed class SettingDto
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string ValueType { get; init; } = "";
    public string? CurrentValue { get; init; }
    public string? DefaultValue { get; init; }
    public bool RequiresRestart { get; init; }
    public string? UiHint { get; init; }
    public string? Visibility { get; init; }
    public SettingSchemaDto? Schema { get; init; }
}

public sealed class SettingSchemaDto
{
    public List<SettingSchemaPropertyDto> Properties { get; init; } = new();
}

public sealed class SettingSchemaPropertyDto
{
    public string Key { get; init; } = "";
    public string Type { get; init; } = "";
    public string Title { get; init; } = "";
    public string? UiHint { get; init; }
    public string? DefaultValue { get; init; }
    public bool Hidden { get; init; }
    public bool ShowInTable { get; init; } = true;
    public string? Visibility { get; init; }
}

public sealed class OptionDto
{
    public string Value { get; init; } = "";
    public string Label { get; init; } = "";
}

// ── saveConfiguration 请求/响应 DTO ──

public sealed class SaveConfigurationRequest
{
    public List<SettingChangeDto> Changes { get; init; } = new();
}

public sealed class SettingChangeDto
{
    public string Key { get; init; } = "";
    public string? Value { get; init; }
}

public sealed class SaveConfigurationResult
{
    public bool RequiresRestart { get; init; }
}
