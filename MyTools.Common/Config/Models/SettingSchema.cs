namespace MyTools.Common.Config.Models;

/// <summary>Runtime schema for an array setting, copied from plugin.json at registration.</summary>
public sealed class SettingSchema
{
    public IReadOnlyList<SettingSchemaProperty> Properties { get; init; } = [];
}

public sealed class SettingSchemaProperty
{
    public string Key { get; init; } = "";
    public string Type { get; init; } = "string";
    public string Title { get; init; } = "";
    public string? UiHint { get; init; }
    public string? DefaultValue { get; init; }
    public bool Hidden => string.Equals(Type, "hidden", StringComparison.OrdinalIgnoreCase);
}
