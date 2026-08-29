using System.IO;
using System.Text.Json;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Manifest;

[TestFixture]
public class PluginManifestV3Test
{
    private const string SampleJson = """
        {
          "id": "settings",
          "version": "0.0.6",
          "protocolVersion": "3.0",
          "entry": "backend/index.mjs",
          "capabilities": ["configuration.write", "clipboard.read"],
          "detail": { "type": "web", "entry": "web/index.html" }
        }
        """;

    [Test]
    public void Deserialize_ShouldParseRootEntry()
    {
        var m = JsonSerializer.Deserialize<PluginManifestV3>(SampleJson, ProtocolJsonOptions.Default)!;

        Assert.That(m.Id, Is.EqualTo("settings"));
        Assert.That(m.Version, Is.EqualTo("0.0.6"));
        Assert.That(m.ProtocolVersion, Is.EqualTo("3.0"));
        Assert.That(m.Entry, Is.EqualTo("backend/index.mjs"));
        Assert.That(m.Capabilities, Is.EqualTo(new[] { "configuration.write", "clipboard.read" }));
    }

    [Test]
    public void Serialize_ShouldUseRootIdAndEntryWireNames()
    {
        var m = JsonSerializer.Deserialize<PluginManifestV3>(SampleJson, ProtocolJsonOptions.Default)!;
        var json = JsonSerializer.Serialize(m, ProtocolJsonOptions.Default);

        Assert.That(json, Does.Contain("\"id\":\"settings\""));
        Assert.That(json, Does.Contain("\"entry\":\"backend/index.mjs\""));
        Assert.That(json, Does.Not.Contain("entries"));
        Assert.That(json, Does.Not.Contain("entryId"));
        Assert.That(json, Does.Not.Contain("nodeEntry"));
    }

    [Test]
    public void Validate_EntryWithoutCapabilities_ShouldPassWithEmpty()
    {
        var m = new PluginManifestV3
        {
            Id = "p", ProtocolVersion = "3.0", Entry = "index.mjs", Capabilities = []
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_MissingId_ShouldFail()
    {
        var m = new PluginManifestV3 { Id = "", ProtocolVersion = "3.0", Entry = "index.mjs" };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.InvalidPayload));
    }

    [Test]
    public void Validate_MissingEntry_ShouldFail()
    {
        var m = new PluginManifestV3 { Id = "p", ProtocolVersion = "3.0", Entry = "" };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void Validate_ProtocolVersionNot3_ShouldFail()
    {
        var m = new PluginManifestV3
        {
            Id = "p", ProtocolVersion = "2.0", Entry = "index.mjs"
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void Validate_OmittedDetail_ShouldPass()
    {
        var m = new PluginManifestV3
        {
            Id = "p", ProtocolVersion = "3.0", Entry = "index.mjs"
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_ListDetail_ShouldPass()
    {
        var m = new PluginManifestV3
        {
            Id = "p",
            ProtocolVersion = "3.0",
            Entry = "index.mjs",
            Detail = new EntryDetailV3 { Type = PluginDetailTypes.List }
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_WebDetailWithoutEntry_ShouldFail()
    {
        var m = new PluginManifestV3
        {
            Id = "p",
            ProtocolVersion = "3.0",
            Entry = "index.mjs",
            Detail = new EntryDetailV3 { Type = PluginDetailTypes.Web }
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.InvalidPayload));
    }

    [Test]
    public void Validate_BasicDetailType_ShouldFail()
    {
        var m = new PluginManifestV3
        {
            Id = "p",
            ProtocolVersion = "3.0",
            Entry = "index.mjs",
            Detail = new EntryDetailV3 { Type = "basic" }
        };

        var result = PluginManifestV3Validator.Validate(m);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("unsupported detail.type"));
    }
}
