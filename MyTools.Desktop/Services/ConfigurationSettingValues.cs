using System.Text.Json;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Plugins;

namespace MyTools.Desktop.Services;

public static class ConfigurationSettingValues
{
    public static bool Owns(PluginId pluginId, ConfigurationSetting setting) =>
        setting.PluginId == pluginId;

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

    public static object? ConvertOwnedJson(ConfigurationSetting setting, JsonElement value)
    {
        if (setting.ValueType == SettingValueTypes.Array)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Array setting value must be a JSON array.");
            }

            return value.Clone();
        }

        var stringValue = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };

        return Convert(setting, stringValue);
    }

    public static int ApplyOwnedValues(
        IConfigurationRegistry registry,
        PluginId pluginId,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var applied = 0;
        foreach (var (name, value) in values)
        {
            var key = pluginId + "." + name;
            var setting = registry.FindSetting(key);
            if (setting == null || setting.IsDisplayOnly || !Owns(pluginId, setting))
            {
                continue;
            }

            setting.CurrentValue = ConvertOwnedJson(setting, value);
            applied++;
        }

        return applied;
    }
}
