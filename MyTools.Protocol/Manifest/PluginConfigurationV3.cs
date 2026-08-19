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
    /// <summary>
    /// When <c>false</c>, the property is omitted from the settings table and only shown in the edit dialog.
    /// Omitted means <c>true</c>.
    /// </summary>
    public bool Table { get; init; } = true;
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
    public const string Path = "path";
    public const string Hidden = "hidden";
    public const string PathFile = "file";
    public const string PathDirectory = "directory";
    public const string PathFileOrDirectory = "fileOrDirectory";

    public static bool IsSettingType(string? type) => Normalize(type) is
        String or Bool or Int or Double or Array or Path;

    public static bool IsPropertyType(string? type) => Normalize(type) is
        String or Bool or Int or Double or Path or Hidden;

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
        Path => PathFileOrDirectory,
        Hidden => "",
        _ => "input"
    };

    public static bool IsPathType(string? type) => Normalize(type) == Path;

    /// <summary>
    /// Picker/validation kind for <see cref="Path"/> settings and columns.
    /// Accepts <c>file</c>, <c>directory</c>, or <c>fileOrDirectory</c>; anything else defaults to file-or-directory.
    /// </summary>
    public static string NormalizePathKind(string? uiHint)
    {
        var hint = (uiHint ?? "").Trim();
        if (string.Equals(hint, PathFile, StringComparison.OrdinalIgnoreCase))
        {
            return PathFile;
        }

        if (string.Equals(hint, PathDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return PathDirectory;
        }

        return PathFileOrDirectory;
    }
}
