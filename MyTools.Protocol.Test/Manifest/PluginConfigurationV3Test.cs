using System.Text.Json;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Manifest;

[TestFixture]
public class PluginConfigurationV3Test
{
    [Test]
    public void Deserialize_ShouldReadArraySettingSchema()
    {
        const string json = """
            {
              "id": "snippet",
              "version": "0.1.0",
              "protocolVersion": "3.0",
              "icon": "mdi-message-text-outline",
              "configuration": [
                {
                  "key": "Phrases",
                  "label": { "key": "Plugin.Snippet.Setting.Phrases", "defaultValue": "Phrases" },
                  "type": "array",
                  "defaultValue": [],
                  "uiHint": "table",
                  "schema": {
                    "properties": [
                      {
                        "key": "trigger",
                        "type": "string",
                        "label": { "key": "Plugin.Snippet.Column.Trigger", "defaultValue": "Trigger" }
                      },
                      {
                        "key": "timestamp",
                        "type": "hidden",
                        "defaultValue": "${DateTime.Now}"
                      },
                      {
                        "key": "content",
                        "type": "string",
                        "uiHint": "textarea",
                        "label": { "key": "Plugin.Snippet.Column.Content", "defaultValue": "Phrase" }
                      }
                    ]
                  }
                }
              ],
              "entries": [{ "id": "snippet", "entry": "backend/index.mjs" }]
            }
            """;

        var m = JsonSerializer.Deserialize<PluginManifestV3>(json, ProtocolJsonOptions.Default)!;

        Assert.That(m.Icon, Is.EqualTo("mdi-message-text-outline"));
        Assert.That(m.Configuration, Has.Count.EqualTo(1));
        var setting = m.Configuration[0];
        Assert.That(setting.Key, Is.EqualTo("Phrases"));
        Assert.That(setting.Type, Is.EqualTo("array"));
        Assert.That(setting.UiHint, Is.EqualTo("table"));
        Assert.That(setting.DefaultValue!.ToJsonString(), Is.EqualTo("[]"));
        Assert.That(setting.Schema!.Properties, Has.Count.EqualTo(3));
        Assert.That(setting.Schema.Properties[1].Type, Is.EqualTo("hidden"));
        Assert.That(setting.Schema.Properties[1].DefaultValue!.ToJsonString(), Is.EqualTo("\"${DateTime.Now}\""));
        Assert.That(PluginManifestV3Validator.Validate(m).IsValid, Is.True);
    }

    [Test]
    public void Validate_ArrayWithoutSchema_ShouldFail()
    {
        var m = new PluginManifestV3
        {
            Id = "p",
            ProtocolVersion = "3.0",
            Entries = [new() { Id = "main", Entry = "index.mjs" }],
            Configuration =
            [
                new() { Key = "Items", Type = "array" }
            ]
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.InvalidPayload));
        Assert.That(result.Error.Message, Does.Contain("schema.properties"));
    }

    [Test]
    public void Validate_DuplicateKeys_ShouldFail()
    {
        var m = new PluginManifestV3
        {
            Id = "p",
            ProtocolVersion = "3.0",
            Entries = [new() { Id = "main", Entry = "index.mjs" }],
            Configuration =
            [
                new() { Key = "Name", Type = "string" },
                new() { Key = "name", Type = "bool" }
            ]
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("duplicate configuration key"));
    }

    [Test]
    public void Validate_UnknownType_ShouldFail()
    {
        var m = new PluginManifestV3
        {
            Id = "p",
            ProtocolVersion = "3.0",
            Entries = [new() { Id = "main", Entry = "index.mjs" }],
            Configuration =
            [
                new() { Key = "Name", Type = "object" }
            ]
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("unsupported type"));
    }

    [Test]
    public void DefaultUiHint_ShouldMatchDeclaredDefaults()
    {
        Assert.That(PluginConfigurationTypes.DefaultUiHint("string"), Is.EqualTo("input"));
        Assert.That(PluginConfigurationTypes.DefaultUiHint("bool"), Is.EqualTo("checkbox"));
        Assert.That(PluginConfigurationTypes.DefaultUiHint("int"), Is.EqualTo("input-number"));
        Assert.That(PluginConfigurationTypes.DefaultUiHint("integer"), Is.EqualTo("input-number"));
        Assert.That(PluginConfigurationTypes.DefaultUiHint("array"), Is.EqualTo("table"));
    }
}
