using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace MyTools.Protocol.Manifest;

/// <summary>
/// A plugin-declared item in <c>plugin.json</c> <c>configuration</c>.
/// Persisted settings use <c>key</c> (stored as <c>{pluginId}.{key}</c>).
/// Display-only <c>h1</c>/<c>h2</c> headings omit <c>key</c>.
/// </summary>
public sealed class PluginConfigurationSettingV3
{
    /// <summary>
    /// Setting name within the plugin category. Required for persisted types; omitted for
    /// display-only <c>h1</c>/<c>h2</c> headings.
    /// </summary>
    public string Key { get; init; } = "";
    public LocalizedNameV3? Label { get; init; }
    public LocalizedNameV3? Description { get; init; }
    public required string Type { get; init; }
    public JsonNode? DefaultValue { get; init; }
    public string? UiHint { get; init; }
    /// <summary>
    /// Visibility condition macro, for example <c>${ChromeEnabled == true}</c>.
    /// The expression may reference sibling configuration keys of the same plugin.
    /// Empty or omitted means the setting is always shown.
    /// </summary>
    public string? Visibility { get; init; }
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
    /// <summary>
    /// Visibility condition macro, for example <c>${isBashScript == true}</c>.
    /// The expression may reference sibling schema property keys of the same row.
    /// Empty or omitted means the field is always shown in the edit dialog.
    /// </summary>
    public string? Visibility { get; init; }
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
    public const string H1 = "h1";
    public const string H2 = "h2";
    public const string PathFile = "file";
    public const string PathDirectory = "directory";
    public const string PathFileOrDirectory = "fileOrDirectory";

    public static bool IsHeadingType(string? type) => Normalize(type) is H1 or H2;

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
