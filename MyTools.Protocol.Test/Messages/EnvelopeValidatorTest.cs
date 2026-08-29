using System.Text.Json;
using System.Text.Json.Nodes;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Messages;

[TestFixture]
public class EnvelopeValidatorTest
{
    private static JsonNode? P(object? o) => o is null ? null : JsonNode.Parse(JsonSerializer.Serialize(o));

    [Test]
    public void Validate_ValidRequest_ShouldReturnOk()
    {
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "id1", TraceId = "t1", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = MessageKind.Request, Route = "plugin.call.save",
            TimeoutMs = 30000, Payload = P(new { x = 1 })
        };

        var result = EnvelopeValidator.Validate(env);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_MissingId_ShouldReturnInvalidPayload()
    {
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "", TraceId = "t1", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = MessageKind.Request, Route = "plugin.call.save", TimeoutMs = 30000
        };

        var result = EnvelopeValidator.Validate(env);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.InvalidPayload));
        Assert.That(result.Error.Message, Does.Contain("id"));
    }

    [Test]
    public void Validate_MissingRoute_ShouldReturnInvalidPayload()
    {
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "id1", TraceId = "t1", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = MessageKind.Request, Route = "", TimeoutMs = 30000
        };

        var result = EnvelopeValidator.Validate(env);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.InvalidPayload));
        Assert.That(result.Error.Message, Does.Contain("route"));
    }

    [Test]
    public void Validate_ResponseWithoutCorrelationId_ShouldReturnInvalidPayload()
    {
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "id1", CorrelationId = null, TraceId = "t1",
            SessionId = "s1", PluginId = "settings", EndpointId = "node-1",
            Kind = MessageKind.Response, Route = "plugin.call.save"
        };

        var result = EnvelopeValidator.Validate(env);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.InvalidPayload));
        Assert.That(result.Error.Message, Does.Contain("correlationId"));
    }

    [Test]
    public void Validate_RequestMustNotCarryError_ShouldReturnInvalidPayload()
    {
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "id1", TraceId = "t1", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = MessageKind.Request, Route = "plugin.call.save", TimeoutMs = 30000,
            Error = BusError.For(ErrorCode.InternalError)
        };

        var result = EnvelopeValidator.Validate(env);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.InvalidPayload));
    }

    [Test]
    public void Validate_InvalidKindValue_ShouldReturnInvalidPayload()
    {
        // Simulate an unknown kind by deserializing from raw JSON with a bad kind string.
        const string bad = """
            {"version":"3.0","id":"id1","traceId":"t1","sessionId":"s1","pluginId":"settings",
             "endpointId":"web-1","kind":"notification","route":"plugin.call.save","timeoutMs":30000}
            """;

        Assert.That(() => JsonSerializer.Deserialize<Envelope>(bad, ProtocolJsonOptions.Default),
            Throws.InstanceOf<JsonException>());
    }
}
