using System.Text.Json;
using System.Text.Json.Nodes;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Messages;

[TestFixture]
public class EnvelopeTest
{
    private static Envelope SampleRequest() => new()
    {
        Version = ProtocolVersion.Current,
        Id = "abc123",
        CorrelationId = null,
        TraceId = "trace1",
        SessionId = "sess1",
        PluginId = "settings",
        EndpointId = "webview-1",
        Kind = MessageKind.Request,
        Route = "plugin.call.saveConfiguration",
        TimeoutMs = 30000,
        Payload = JsonNode.Parse("""{"key":"value"}"""),
        Error = null
    };

    [Test]
    public void Serialize_Request_ShouldEmitCamelCaseFieldsAndOmitNulls()
    {
        var env = SampleRequest();

        var json = JsonSerializer.Serialize(env, ProtocolJsonOptions.Default);

        Assert.That(json, Does.Contain("\"version\":\"3.0\""));
        Assert.That(json, Does.Contain("\"id\":\"abc123\""));
        Assert.That(json, Does.Contain("\"traceId\":\"trace1\""));
        Assert.That(json, Does.Contain("\"sessionId\":\"sess1\""));
        Assert.That(json, Does.Contain("\"pluginId\":\"settings\""));
        Assert.That(json, Does.Not.Contain("\"entryId\""));
        Assert.That(json, Does.Contain("\"endpointId\":\"webview-1\""));
        Assert.That(json, Does.Contain("\"kind\":\"request\""));
        Assert.That(json, Does.Contain("\"route\":\"plugin.call.saveConfiguration\""));
        Assert.That(json, Does.Contain("\"timeoutMs\":30000"));
        Assert.That(json, Does.Contain("\"payload\":{\"key\":\"value\"}"));
        // Null fields (correlationId, error) are omitted from the wire, not emitted as null.
        Assert.That(json, Does.Not.Contain("\"correlationId\""));
        Assert.That(json, Does.Not.Contain("\"error\""));
    }

    [Test]
    public void RoundTrip_ShouldPreserveAllFields()
    {
        var env = SampleRequest();

        var json = JsonSerializer.Serialize(env, ProtocolJsonOptions.Default);
        var back = JsonSerializer.Deserialize<Envelope>(json, ProtocolJsonOptions.Default)!;

        Assert.That(back.Id, Is.EqualTo("abc123"));
        Assert.That(back.TraceId, Is.EqualTo("trace1"));
        Assert.That(back.SessionId, Is.EqualTo("sess1"));
        Assert.That(back.PluginId, Is.EqualTo("settings"));
        Assert.That(back.EndpointId, Is.EqualTo("webview-1"));
        Assert.That(back.Kind, Is.EqualTo(MessageKind.Request));
        Assert.That(back.Route, Is.EqualTo("plugin.call.saveConfiguration"));
        Assert.That(back.TimeoutMs, Is.EqualTo(30000));
        Assert.That(back.CorrelationId, Is.Null);
        Assert.That(back.Error, Is.Null);
        Assert.That(back.Payload!["key"]!.GetValue<string>(), Is.EqualTo("value"));
    }

    [Test]
    public void Serialize_Response_ShouldCarryCorrelationId()
    {
        var env = new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = "resp1",
            CorrelationId = "abc123",
            TraceId = "trace1",
            SessionId = "sess1",
            PluginId = "settings",
            EndpointId = "node-main",
            Kind = MessageKind.Response,
            Route = "plugin.call.saveConfiguration",
            TimeoutMs = null,
            Payload = JsonNode.Parse("{}"),
            Error = null
        };

        var json = JsonSerializer.Serialize(env, ProtocolJsonOptions.Default);

        Assert.That(json, Does.Contain("\"correlationId\":\"abc123\""));
        // Null timeoutMs omitted on responses.
        Assert.That(json, Does.Not.Contain("\"timeoutMs\""));
    }

    [Test]
    public void Serialize_ErrorResponse_ShouldCarryErrorObject()
    {
        var env = new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = "resp2",
            CorrelationId = "abc123",
            TraceId = "trace1",
            SessionId = "sess1",
            PluginId = "settings",
            EndpointId = "node-main",
            Kind = MessageKind.Response,
            Route = "plugin.call.saveConfiguration",
            TimeoutMs = null,
            Payload = null,
            Error = BusError.For(ErrorCode.RequestTimeout, "timed out")
        };

        var json = JsonSerializer.Serialize(env, ProtocolJsonOptions.Default);

        Assert.That(json, Does.Contain("\"code\":\"RequestTimeout\""));
        Assert.That(json, Does.Contain("\"message\":\"timed out\""));
    }

    [Test]
    public void Deserialize_ShouldPreserveVersionString()
    {
        var env = SampleRequest();

        var json = JsonSerializer.Serialize(env, ProtocolJsonOptions.Default);
        var back = JsonSerializer.Deserialize<Envelope>(json, ProtocolJsonOptions.Default)!;

        Assert.That(back.Version.Major, Is.EqualTo(3));
        Assert.That(back.Version.Minor, Is.EqualTo(0));
    }
}
