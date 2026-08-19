using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace MyTools.Protocol.Manifest;

/// <summary>
/// A plugin-declared setting in <c>plugin.json</c> <c>configuration</c>.
/// <c>key</c> is the setting name within the plugin category; the host stores it as
/// <c>{pluginId}.{key}</c> (for example <c>snippet.Phrases</c>).
/// </summary>
public sealed class PluginConfigurationSettingV3
{
    public required string Key { get; init; }
    public LocalizedNameV3? Label { get; init; }
    public LocalizedNameV3? Description { get; init; }
    public required string Type { get; init; }
    public JsonNode? DefaultValue { get; init; }
    public string? UiHint { get; init; }
    public PluginConfigurationSchemaV3? Schema { get; init; }
}

/// <summary>Item schema for <c>type: array</c> settings rendered as a table.</summary>
public sealed class PluginConfigurationSchemaV3
{
    public IReadOnlyList<PluginConfigurationPropertyV3> Properties { get; init; } = [];
}

/// <summary>One column / editor field in an array setting.</summary>
public sealed class PluginConfigurationPropertyV3
{
    public required string Key { get; init; }
    public required string Type { get; init; }
    public LocalizedNameV3? Label { get; init; }
    public JsonNode? DefaultValue { get; init; }
    public string? UiHint { get; init; }
}

/// <summary>Allowed <c>configuration[].type</c> values in plugin.json.</summary>
public static class PluginConfigurationTypes
{
    public const string String = "string";
    public const string Bool = "bool";
    public const string Int = "int";
    public const string Integer = "integer";
    public const string Double = "double";
    public const string Array = "array";
    public const string Hidden = "hidden";

    public static bool IsSettingType(string? type) => Normalize(type) is
        String or Bool or Int or Double or Array;

    public static bool IsPropertyType(string? type) => Normalize(type) is
        String or Bool or Int or Double or Hidden;

    public static string Normalize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "";
        }

        var trimmed = type.Trim().ToLowerInvariant();
        return trimmed == Integer ? Int : trimmed;
    }

    public static string DefaultUiHint(string? type) => Normalize(type) switch
    {
        Bool => "checkbox",
        Int or Double => "input-number",
        Array => "table",
        Hidden => "",
        _ => "input"
    };
}
