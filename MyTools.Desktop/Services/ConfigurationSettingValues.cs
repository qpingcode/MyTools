using System.Text.Json;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Models;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public static class ConfigurationSettingValues
{
    public static bool Owns(string pluginId, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        return fullPath.StartsWith(pluginId + ".", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ToDtoString(object? value)
    {
        return value switch
        {
            null => null,
            bool b => b ? "True" : "False",
            JsonElement json => json.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : json.GetRawText(),
            _ => value.ToString()
        };
    }

    public static JsonElement ToJsonElement(object? value)
    {
        return value switch
        {
            JsonElement json when json.ValueKind is not JsonValueKind.Undefined => json.Clone(),
            null => JsonSerializer.SerializeToElement((object?)null),
            _ => JsonSerializer.SerializeToElement(value)
        };
    }

    public static object? Convert(ConfigurationSetting setting, string? stringValue)
    {
        if (stringValue == null)
        {
            return null;
        }

        return setting.ValueType switch
        {
            SettingValueTypes.Bool => string.Equals(stringValue, "True", StringComparison.OrdinalIgnoreCase),
            SettingValueTypes.Integer => ConvertOrDefault(stringValue, setting, int.TryParse, 0),
            SettingValueTypes.Double => ConvertOrDefault(stringValue, setting, double.TryParse, 0d),
            SettingValueTypes.Array => ParseArray(stringValue),
            _ => stringValue
        };
    }

    private static object ConvertOrDefault<T>(
        string stringValue,
        ConfigurationSetting setting,
        TryParseHandler<T> tryParse,
        T fallback)
        where T : struct
    {
        if (!string.IsNullOrWhiteSpace(stringValue) && tryParse(stringValue, out var parsed))
        {
            return parsed;
        }

        return setting.DefaultValue is T defaultValue ? defaultValue : fallback;
    }

    private delegate bool TryParseHandler<T>(string value, out T result);

    private static JsonElement ParseArray(string stringValue)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(stringValue) ? "[]" : stringValue);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Array setting value must be a JSON array.");
        }

        return document.RootElement.Clone();
    }
}
