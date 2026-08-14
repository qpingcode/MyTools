using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Handshake;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Sessions;

[TestFixture]
public class PipeHandshakeTest
{
    private static readonly ProcessIdentity Identity =
        new(99, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "settings", "main");

    [Test]
    public async Task CompleteAsHost_ValidToken_ShouldReplyWithBoundIdentity()
    {
        var transport = new InMemoryTransport();
        var tokens = new BootstrapTokenValidator(() => Identity.CreationTime);
        var issued = tokens.Issue(Identity, TimeSpan.FromSeconds(30));
        var ids = new GuidIdGenerator();

        var payload = HandshakePayload.BuildNamedPipeRequest(PipeHandshake.HostSupportedVersions, issued.Value);
        transport.Deliver(HandshakeRequest(payload));

        var negotiated = await PipeHandshake.CompleteAsHostAsync(
            transport, tokens, Identity, "sess-1", "node-main", ids, TimeSpan.FromSeconds(2), default);

        Assert.That(negotiated, Is.EqualTo(ProtocolVersion.Current));
        Assert.That(transport.Sent, Has.Count.EqualTo(1));
        var reply = transport.Sent.ToArray()[0];
        Assert.That(reply.Route, Is.EqualTo("bus.handshake"));
        Assert.That(reply.CorrelationId, Is.EqualTo("hs-1"));
        Assert.That(reply.Error, Is.Null);
        var body = reply.Payload!.Deserialize<HandshakePayload>(ProtocolJsonOptions.Default)!;
        Assert.That(body.PluginId, Is.EqualTo("settings"));
        Assert.That(body.EntryId, Is.EqualTo("main"));
        Assert.That(body.SessionId, Is.EqualTo("sess-1"));
        Assert.That(body.EndpointId, Is.EqualTo("node-main"));
    }

    [Test]
    public async Task CompleteAsHost_ReplayedToken_ShouldFailHandshake()
    {
        var transport = new InMemoryTransport();
        var tokens = new BootstrapTokenValidator(() => Identity.CreationTime);
        var issued = tokens.Issue(Identity, TimeSpan.FromSeconds(30));
        tokens.Validate(issued.Value, Identity); // consume once
        var ids = new GuidIdGenerator();

        var payload = HandshakePayload.BuildNamedPipeRequest(PipeHandshake.HostSupportedVersions, issued.Value);
        transport.Deliver(HandshakeRequest(payload));

        var ex = Assert.ThrowsAsync<HandshakeException>(async () =>
            await PipeHandshake.CompleteAsHostAsync(
                transport, tokens, Identity, "sess-1", "node-main", ids, TimeSpan.FromSeconds(2), default));

        Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.HandshakeFailed));
        Assert.That(transport.Sent, Has.Count.EqualTo(1));
        Assert.That(transport.Sent.ToArray()[0].Error!.Code, Is.EqualTo(ErrorCode.HandshakeFailed));
    }

    [Test]
    public async Task CompleteAsHost_MajorMismatch_ShouldReturnProtocolMismatch()
    {
        var transport = new InMemoryTransport();
        var tokens = new BootstrapTokenValidator(() => Identity.CreationTime);
        var issued = tokens.Issue(Identity, TimeSpan.FromSeconds(30));
        var ids = new GuidIdGenerator();

        var payload = new HandshakePayload
        {
            Version = new ProtocolVersion(9, 0),
            SupportedVersions = [new ProtocolVersion(9, 0)],
            Token = issued.Value,
        };
        transport.Deliver(HandshakeRequest(payload));

        var ex = Assert.ThrowsAsync<HandshakeException>(async () =>
            await PipeHandshake.CompleteAsHostAsync(
                transport, tokens, Identity, "sess-1", "node-main", ids, TimeSpan.FromSeconds(2), default));

        Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ProtocolMismatch));
    }

    private static Envelope HandshakeRequest(HandshakePayload payload) => new()
    {
        Version = ProtocolVersion.Current,
        Id = "hs-1",
        TraceId = "hs-1",
        SessionId = "",
        PluginId = "settings",
        EntryId = "main",
        EndpointId = "node-main",
        Kind = MessageKind.Request,
        Route = "bus.handshake",
        TimeoutMs = 5000,
        Payload = JsonSerializer.SerializeToNode(payload, ProtocolJsonOptions.Default),
    };
}
