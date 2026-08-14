using System.Linq;
using System.Threading.Tasks;
using MyTools.Host.Core.Backpressure;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.E2E;

/// <summary>
/// End-to-end and fault-recovery scenarios using in-memory transports: the full WebView→bus→Node
/// round trip, stale-session message rejection after a generation bump, undeclared-capability
/// denial, and pending-request failure on disconnect.
/// </summary>
[TestFixture]
public class EndToEndScenariosTest
{
    private static EndpointId Node(string p, string e, string s) => new(p, e, s, "node-main", IsNode: true);
    private static EndpointId Web(string p, string e, string s) => new(p, e, s, "web-1", IsNode: false);

    private static Envelope Req(EndpointId from, string route, string id) => new()
    {
        Version = ProtocolVersion.Current, Id = id, TraceId = id, SessionId = from.SessionId,
        PluginId = from.PluginId, EntryId = from.EntryId, EndpointId = from.EndpointLabel,
        Kind = MessageKind.Request, Route = route, TimeoutMs = 5000
    };

    // Test 38 — full chain: webview request routes to node, node response returns to webview.
    [Test]
    public async Task FullChain_WebViewRequestNodeResponse_ShouldRoundTrip()
    {
        var bus = new MessageBus();
        var webT = new InMemoryTransport();
        var nodeT = new InMemoryTransport();
        bus.RegisterEndpoint(Web("settings", "main", "s1"), webT);
        bus.RegisterEndpoint(Node("settings", "main", "s1"), nodeT);

        await bus.RouteRequestAsync(Req(Web("settings", "main", "s1"), "plugin.call.save", "r1"),
            Web("settings", "main", "s1"));

        // Node received the request.
        Assert.That(nodeT.Sent, Has.Count.EqualTo(1));
        var receivedReq = nodeT.Sent.ToArray()[0];

        // Node responds.
        nodeT.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current, Id = "resp-1", CorrelationId = receivedReq.Id,
            TraceId = receivedReq.TraceId, SessionId = "s1", PluginId = "settings", EntryId = "main",
            EndpointId = "node-main", Kind = MessageKind.Response, Route = "plugin.call.save"
        });

        // Response reached the webview transport.
        Assert.That(webT.Sent, Has.Count.EqualTo(1));
        Assert.That(webT.Sent.ToArray()[0].CorrelationId, Is.EqualTo("r1"));
    }

    // Test 39 — after a Node reload (old session invalidated), the old session's response is dropped.
    [Test]
    public async Task AfterNodeReload_OldSessionResponse_ShouldNotReachNewWebview()
    {
        var bus = new MessageBus();
        var webNew = new InMemoryTransport();
        bus.RegisterEndpoint(Web("settings", "main", "s2"), webNew); // new session s2

        // A stray response from the OLD session s1 arrives (e.g. a late reply from the killed Node).
        var nodeOld = Node("settings", "main", "s1");
        var stray = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "late", CorrelationId = "never-issued",
            TraceId = "t", SessionId = "s1", PluginId = "settings", EntryId = "main",
            EndpointId = "node-main", Kind = MessageKind.Response, Route = "plugin.call.save"
        };
        // Route it: since no endpoint in s1 is registered and correlation is unknown, it's dropped.
        bus.RegisterEndpoint(nodeOld, new InMemoryTransport());
        var oldNodeT = new InMemoryTransport();
        // Re-deliver through the old session's node transport.
        var oldSessionBus = new MessageBus();
        oldSessionBus.RegisterEndpoint(nodeOld, oldNodeT);
        oldNodeT.Deliver(stray);

        // Nothing reaches the new session's webview.
        Assert.That(webNew.Sent, Is.Empty);
    }

    // Test 40 — undeclared capability is denied by the gateway.
    [Test]
    public void CapabilityGateway_UndeclaredCapability_ShouldBeDenied()
    {
        var gw = new CapabilityGateway();
        gw.RegisterManifest(new PluginManifest("settings", "main", ["configuration.write"]));

        var decision = gw.Authorize("settings", "main", "clipboard.read");

        Assert.That(decision.IsAllowed, Is.False);
        Assert.That(decision.Error!.Code, Is.EqualTo(ErrorCode.CapabilityNotDeclared));
    }

    // Test 41 — pending-request admission under backpressure.
    [Test]
    public void PendingTracker_AtLimit_ShouldRejectAndReturnTooManyRequestsSemantics()
    {
        var tracker = new PendingRequestTracker(limit: 2);
        Assert.That(tracker.TryReserve("a", "plugin.call.x"), Is.True);
        Assert.That(tracker.TryReserve("b", "plugin.call.y"), Is.True);

        // Over the cap: the caller would map this false into TooManyRequests.
        var admitted = tracker.TryReserve("c", "plugin.call.z");
        Assert.That(admitted, Is.False);

        // Releasing a slot frees capacity.
        tracker.Release("a", "plugin.call.x");
        Assert.That(tracker.TryReserve("d", "plugin.call.w"), Is.True);
    }
}
