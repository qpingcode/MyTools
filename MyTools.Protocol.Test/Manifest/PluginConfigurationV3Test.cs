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
    public void Deserialize_ShouldReadSchemaPropertyVisibility()
    {
        const string json = """
            {
              "id": "command-runner",
              "version": "0.1.0",
              "protocolVersion": "3.0",
              "configuration": [
                {
                  "key": "Commands",
                  "type": "array",
                  "schema": {
                    "properties": [
                      { "key": "name", "type": "string" },
                      { "key": "isBashScript", "type": "bool" },
                      {
                        "key": "command",
                        "type": "string",
                        "table": false,
                        "visibility": "${isBashScript == false}"
                      },
                      {
                        "key": "scripts",
                        "type": "string",
                        "table": false,
                        "visibility": "${isBashScript == true}"
                      }
                    ]
                  }
                }
              ],
              "entries": [{ "id": "command-runner", "entry": "backend/index.mjs" }]
            }
            """;

        var m = JsonSerializer.Deserialize<PluginManifestV3>(json, ProtocolJsonOptions.Default)!;
        var schema = m.Configuration[0].Schema!;

        Assert.That(schema.Properties.Single(property => property.Key == "command").Visibility,
            Is.EqualTo("${isBashScript == false}"));
        Assert.That(schema.Properties.Single(property => property.Key == "scripts").Visibility,
            Is.EqualTo("${isBashScript == true}"));
        Assert.That(schema.Properties.Single(property => property.Key == "command").Table, Is.False);
        Assert.That(schema.Properties.Single(property => property.Key == "name").Table, Is.True);
        Assert.That(PluginManifestV3Validator.Validate(m).IsValid, Is.True);
    }

    [Test]
    public void Deserialize_ShouldReadVisibilityCondition()
    {
        const string json = """
            {
              "id": "browser-search",
              "version": "0.1.0",
              "protocolVersion": "3.0",
              "configuration": [
                { "key": "ChromeEnabled", "type": "bool", "defaultValue": true },
                {
                  "key": "ChromeProfile",
                  "type": "string",
                  "visibility": "${ChromeEnabled == true}"
                }
              ],
              "entries": [{ "id": "main", "entry": "backend/index.mjs" }]
            }
            """;

        var m = JsonSerializer.Deserialize<PluginManifestV3>(json, ProtocolJsonOptions.Default)!;

        Assert.That(m.Configuration[1].Visibility, Is.EqualTo("${ChromeEnabled == true}"));
        Assert.That(PluginManifestV3Validator.Validate(m).IsValid, Is.True);
    }

    [Test]
    public void Deserialize_ShouldReadHeadingWithoutKey()
    {
        const string json = """
            {
              "id": "command-runner",
              "version": "0.1.0",
              "protocolVersion": "3.0",
              "configuration": [
                {
                  "label": { "key": "Plugin.CommandRunner.Name", "defaultValue": "Custom Commands" },
                  "description": { "key": "d", "defaultValue": "Configure commands." },
                  "type": "h1"
                },
                {
                  "key": "Commands",
                  "type": "array",
                  "schema": { "properties": [{ "key": "name", "type": "string" }] }
                }
              ],
              "entries": [{ "id": "command-runner", "entry": "backend/index.mjs" }]
            }
            """;

        var m = JsonSerializer.Deserialize<PluginManifestV3>(json, ProtocolJsonOptions.Default)!;

        Assert.That(m.Configuration[0].Type, Is.EqualTo("h1"));
        Assert.That(m.Configuration[0].Key, Is.Null.Or.Empty);
        Assert.That(m.Configuration[0].Label?.DefaultValue, Is.EqualTo("Custom Commands"));
        Assert.That(PluginManifestV3Validator.Validate(m).IsValid, Is.True);
    }

    [Test]
    public void Validate_HeadingWithSchema_ShouldFail()
    {
        var m = new PluginManifestV3
        {
            Id = "p",
            ProtocolVersion = "3.0",
            Entries = [new() { Id = "main", Entry = "index.mjs" }],
            Configuration =
            [
                new()
                {
                    Type = "h2",
                    Schema = new PluginConfigurationSchemaV3
                    {
                        Properties = [new() { Key = "name", Type = "string" }]
                    }
                }
            ]
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("heading"));
    }

    [Test]
    public void Validate_SettingWithoutKey_ShouldFail()
    {
        var m = new PluginManifestV3
        {
            Id = "p",
            ProtocolVersion = "3.0",
            Entries = [new() { Id = "main", Entry = "index.mjs" }],
            Configuration =
            [
                new() { Type = "bool" }
            ]
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("missing key"));
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
        Assert.That(PluginConfigurationTypes.DefaultUiHint("path"), Is.EqualTo("fileOrDirectory"));
        Assert.That(PluginConfigurationTypes.NormalizePathKind("file"), Is.EqualTo("file"));
        Assert.That(PluginConfigurationTypes.NormalizePathKind("directory"), Is.EqualTo("directory"));
        Assert.That(PluginConfigurationTypes.NormalizePathKind(""), Is.EqualTo("fileOrDirectory"));
        Assert.That(PluginConfigurationTypes.NormalizePathKind("input"), Is.EqualTo("fileOrDirectory"));
    }

    [Test]
    public void Validate_PathType_ShouldPass()
    {
        var m = new PluginManifestV3
        {
            Id = "p",
            ProtocolVersion = "3.0",
            Entries = [new() { Id = "main", Entry = "index.mjs" }],
            Configuration =
            [
                new() { Key = "InstallPath", Type = "path", UiHint = "directory" }
            ]
        };

        Assert.That(PluginManifestV3Validator.Validate(m).IsValid, Is.True);
    }
}
