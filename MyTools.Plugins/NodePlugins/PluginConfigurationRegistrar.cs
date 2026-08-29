using System.Text.Json;
using System.Text.Json.Nodes;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Localization;
using MyTools.Common.Plugins;
using MyTools.Protocol.Manifest;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// Registers plugin.json <c>configuration</c> into the host settings registry.
/// </summary>
public static class PluginConfigurationRegistrar
{
    public static void Register(
        IConfigurationRegistry registry,
        string pluginId,
        string categoryName,
        string categoryDescription,
        IReadOnlyList<PluginConfigurationSettingV3> configuration,
        ILocalizationService localization,
        string? icon = null)
    {
        var ownerId = new PluginId(pluginId);
        if (configuration.Count == 0 || registry.FindCategory(ownerId.Value) != null)
        {
            return;
        }

        var category = registry.AddCategory(
            ownerId.Value,
            categoryName,
            categoryDescription,
            pluginId: ownerId);
        category.Icon = NormalizeIcon(icon);
        foreach (var item in configuration)
        {
            RegisterSetting(registry, category, item, localization);
        }
    }

    public static string? NormalizeIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        var trimmed = icon.Trim();
        return trimmed.StartsWith("mdi-", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : "mdi-" + trimmed;
    }

    private static void RegisterSetting(
        IConfigurationRegistry registry,
        ConfigurationCategory category,
        PluginConfigurationSettingV3 item,
        ILocalizationService localization)
    {
        var normalizedType = PluginConfigurationTypes.Normalize(item.Type);
        if (PluginConfigurationTypes.IsHeadingType(normalizedType))
        {
            RegisterHeading(registry, category, item, localization, normalizedType);
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Key))
        {
            return;
        }

        var name = item.Key.Trim();
        var title = ResolveLabel(item.Label, "", localization);
        var description = ResolveLabel(item.Description, "", localization);
        var uiHint = string.IsNullOrWhiteSpace(item.UiHint)
            ? PluginConfigurationTypes.DefaultUiHint(normalizedType)
            : item.UiHint.Trim();

        ConfigurationSetting setting;
        if (normalizedType == PluginConfigurationTypes.Array)
        {
            var defaultJson = ToJsonElement(item.DefaultValue, JsonValueKind.Array);
            setting = registry.AddSetting(
                category,
                name,
                title,
                description,
                defaultJson,
                new JsonElementSettingSerializer(),
                valueType: SettingValueTypes.Array);
            setting.Schema = MapSchema(item.Schema, localization);
        }
        else if (normalizedType == PluginConfigurationTypes.Path)
        {
            setting = registry.AddSetting(
                category,
                name,
                title,
                description,
                ToStringValue(item.DefaultValue),
                valueType: SettingValueTypes.Path);
            uiHint = PluginConfigurationTypes.NormalizePathKind(uiHint);
        }
        else
        {
            setting = RegisterScalar(registry, category, name, title, description, normalizedType, item.DefaultValue);
        }

        setting.UiHint = uiHint;
        setting.Visibility = string.IsNullOrWhiteSpace(item.Visibility) ? null : item.Visibility.Trim();
    }

    private static void RegisterHeading(
        IConfigurationRegistry registry,
        ConfigurationCategory category,
        PluginConfigurationSettingV3 item,
        ILocalizationService localization,
        string normalizedType)
    {
        var name = $"__heading_{normalizedType}_{category.Settings.Count}";
        var title = ResolveLabel(item.Label, "", localization);
        var description = ResolveLabel(item.Description, "", localization);
        var valueType = normalizedType == PluginConfigurationTypes.H2
            ? SettingValueTypes.H2
            : SettingValueTypes.H1;
        var setting = registry.AddSetting(
            category,
            name,
            title,
            description,
            string.Empty,
            options: SettingOptions.DisplayOnly,
            valueType: valueType);
        setting.Visibility = string.IsNullOrWhiteSpace(item.Visibility) ? null : item.Visibility.Trim();
    }

    private static SchemaPropertyType ToSchemaPropertyType(string type)
    {
        return PluginConfigurationTypes.Normalize(type) switch
        {
            PluginConfigurationTypes.Bool => SchemaPropertyType.Bool,
            PluginConfigurationTypes.Int => SchemaPropertyType.Int,
            PluginConfigurationTypes.Double => SchemaPropertyType.Double,
            PluginConfigurationTypes.Path => SchemaPropertyType.Path,
            PluginConfigurationTypes.Hidden => SchemaPropertyType.Hidden,
            _ => SchemaPropertyType.String
        };
    }

    private static ConfigurationSetting RegisterScalar(
        IConfigurationRegistry registry,
        ConfigurationCategory category,
        string name,
        string title,
        string description,
        string normalizedType,
        JsonNode? defaultValue)
    {
        return normalizedType switch
        {
            PluginConfigurationTypes.Bool => registry.AddSetting(
                category, name, title, description, ToBool(defaultValue)),
            PluginConfigurationTypes.Int => registry.AddSetting(
                category, name, title, description, ToInt(defaultValue)),
            PluginConfigurationTypes.Double => registry.AddSetting(
                category, name, title, description, ToDouble(defaultValue)),
            _ => registry.AddSetting(
                category, name, title, description, ToStringValue(defaultValue))
        };
    }

    private static SettingSchema MapSchema(PluginConfigurationSchemaV3? schema, ILocalizationService localization)
    {
        var properties = schema?.Properties ?? [];
        return new SettingSchema
        {
            Properties = properties.Select(property =>
            {
                var type = ToSchemaPropertyType(property.Type);

                var uiHint = string.IsNullOrWhiteSpace(property.UiHint)
                    ? PluginConfigurationTypes.DefaultUiHint(type.ToWireString())
                    : property.UiHint.Trim();
                if (PluginConfigurationTypes.IsPathType(type.ToWireString()))
                {
                    uiHint = PluginConfigurationTypes.NormalizePathKind(uiHint);
                }
                return new SettingSchemaProperty
                {
                    Key = property.Key,
                    Type = type,
                    Title = ResolveLabel(property.Label, property.Key, localization),
                    UiHint = string.IsNullOrEmpty(uiHint) ? null : uiHint,
                    DefaultValue = ToStringValue(property.DefaultValue),
                    ShowInTable = property.ShowInTable,
                    Visibility = string.IsNullOrWhiteSpace(property.Visibility) ? null : property.Visibility.Trim()
                };
            }).ToList()
        };
    }

    private static string ResolveLabel(LocalizedNameV3? label, string fallback, ILocalizationService localization)
    {
        if (label == null || (string.IsNullOrWhiteSpace(label.Key) && string.IsNullOrWhiteSpace(label.DefaultValue)))
        {
            return fallback;
        }

        return new LocalizedMessage(label.Key, string.IsNullOrWhiteSpace(label.DefaultValue) ? fallback : label.DefaultValue)
            .Resolve(localization);
    }

    private static JsonElement ToJsonElement(JsonNode? node, JsonValueKind fallbackKind)
    {
        if (node != null)
        {
            return JsonSerializer.SerializeToElement(node);
        }

        return fallbackKind == JsonValueKind.Array
            ? JsonSerializer.SerializeToElement(Array.Empty<object>())
            : default;
    }

    private static bool ToBool(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var b) && b;

    private static int ToInt(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var i))
            {
                return i;
            }

            if (value.TryGetValue<double>(out var d))
            {
                return (int)d;
            }
        }

        return 0;
    }

    private static double ToDouble(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<double>(out var d))
        {
            return d;
        }

        return 0;
    }

    private static string ToStringValue(JsonNode? node)
    {
        if (node == null)
        {
            return string.Empty;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return node.ToJsonString();
    }
}
