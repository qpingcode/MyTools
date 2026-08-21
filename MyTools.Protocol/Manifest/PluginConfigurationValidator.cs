using System.Collections.Generic;
using MyTools.Protocol.Errors;

namespace MyTools.Protocol.Manifest;

public static class PluginConfigurationValidator
{
    public static ManifestValidation Validate(IReadOnlyList<PluginConfigurationSettingV3>? configuration)
    {
        if (configuration is null || configuration.Count == 0)
        {
            return ManifestValidation.Ok();
        }

        var settingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in configuration)
        {
            if (PluginConfigurationTypes.IsHeadingType(setting.Type))
            {
                if (setting.Schema?.Properties is { Count: > 0 })
                {
                    return ManifestValidation.Fail("heading configuration must not declare schema");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(setting.Key))
            {
                return ManifestValidation.Fail("configuration item is missing key");
            }

            if (!settingKeys.Add(setting.Key.Trim()))
            {
                return ManifestValidation.Fail($"duplicate configuration key '{setting.Key}'");
            }

            if (!PluginConfigurationTypes.IsSettingType(setting.Type))
            {
                return ManifestValidation.Fail(
                    $"configuration '{setting.Key}' has unsupported type '{setting.Type}'");
            }

            var normalized = PluginConfigurationTypes.Normalize(setting.Type);
            if (normalized == PluginConfigurationTypes.Array)
            {
                var schemaResult = ValidateSchema(setting.Key, setting.Schema);
                if (!schemaResult.IsValid)
                {
                    return schemaResult;
                }
            }
        }

        return ManifestValidation.Ok();
    }

    private static ManifestValidation ValidateSchema(string settingKey, PluginConfigurationSchemaV3? schema)
    {
        if (schema?.Properties is null || schema.Properties.Count == 0)
        {
            return ManifestValidation.Fail($"configuration '{settingKey}' of type array requires schema.properties");
        }

        var propertyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in schema.Properties)
        {
            if (string.IsNullOrWhiteSpace(property.Key))
            {
                return ManifestValidation.Fail($"configuration '{settingKey}' has a schema property without key");
            }

            if (!propertyKeys.Add(property.Key.Trim()))
            {
                return ManifestValidation.Fail(
                    $"configuration '{settingKey}' has duplicate schema property '{property.Key}'");
            }

            if (!PluginConfigurationTypes.IsPropertyType(property.Type))
            {
                return ManifestValidation.Fail(
                    $"configuration '{settingKey}' property '{property.Key}' has unsupported type '{property.Type}'");
            }
        }

        return ManifestValidation.Ok();
    }
}
