using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Fixtures;

/// <summary>
/// These fixtures are the canonical wire-format samples. C# serialization MUST reproduce them
/// byte-for-byte (modulo trailing whitespace). When the TypeScript SDK lands (Step 5), it will be
/// fed the same files to expose any hand-written type drift between the two sides in CI.
/// </summary>
[TestFixture]
public class SampleFixturesTest
{
    private static readonly string FixtureDir =
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

    private static string Canonicalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string ReadFixture(string name)
        => File.ReadAllText(Path.Combine(FixtureDir, name)).Trim();

    [Test]
    public void RequestFixture_ShouldRoundTripAndMatchWireFormat()
    {
        var fixture = ReadFixture("sample-request.json");

        var env = JsonSerializer.Deserialize<Envelope>(fixture, ProtocolJsonOptions.Default)!;
        var reserialized = JsonSerializer.Serialize(env, ProtocolJsonOptions.Default);

        Assert.That(Canonicalize(reserialized), Is.EqualTo(Canonicalize(fixture)));
        Assert.That(env.Id, Is.EqualTo("req-001"));
        Assert.That(env.Kind, Is.EqualTo(MessageKind.Request));
        Assert.That(env.Payload!["key"]!.GetValue<string>(), Is.EqualTo("theme"));
    }

    [Test]
    public void SuccessResponseFixture_ShouldRoundTripAndMatchWireFormat()
    {
        var fixture = ReadFixture("sample-response-success.json");

        var env = JsonSerializer.Deserialize<Envelope>(fixture, ProtocolJsonOptions.Default)!;
        var reserialized = JsonSerializer.Serialize(env, ProtocolJsonOptions.Default);

        Assert.That(Canonicalize(reserialized), Is.EqualTo(Canonicalize(fixture)));
        Assert.That(env.CorrelationId, Is.EqualTo("req-001"));
        Assert.That(env.TimeoutMs, Is.Null);
        Assert.That(env.Payload!["saved"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void ErrorResponseFixture_ShouldRoundTripAndMatchWireFormat()
    {
        var fixture = ReadFixture("sample-response-error.json");

        var env = JsonSerializer.Deserialize<Envelope>(fixture, ProtocolJsonOptions.Default)!;
        var reserialized = JsonSerializer.Serialize(env, ProtocolJsonOptions.Default);

        Assert.That(Canonicalize(reserialized), Is.EqualTo(Canonicalize(fixture)));
        Assert.That(env.Error!.Code, Is.EqualTo(ErrorCode.RequestTimeout));
        Assert.That(env.Error.Retryable, Is.False);
        Assert.That(env.Payload, Is.Null);
    }

    [Test]
    public void EventFixture_ShouldRoundTripAndMatchWireFormat()
    {
        var fixture = ReadFixture("sample-event.json");

        var env = JsonSerializer.Deserialize<Envelope>(fixture, ProtocolJsonOptions.Default)!;
        var reserialized = JsonSerializer.Serialize(env, ProtocolJsonOptions.Default);

        Assert.That(Canonicalize(reserialized), Is.EqualTo(Canonicalize(fixture)));
        Assert.That(env.Kind, Is.EqualTo(MessageKind.Event));
        Assert.That(env.TraceId, Is.EqualTo(env.Id)); // standalone events use their own id as trace
        Assert.That(env.TimeoutMs, Is.Null);
    }
}
