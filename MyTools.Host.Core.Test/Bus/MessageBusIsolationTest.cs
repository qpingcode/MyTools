using System.Threading.Tasks;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Bus;

[TestFixture]
public class MessageBusIsolationTest
{
    private static Envelope Request(EndpointId from, string route, string id) => new()
    {
        Version = ProtocolVersion.Current, Id = id, TraceId = id, SessionId = from.SessionId,
        PluginId = from.PluginId, EntryId = from.EntryId, EndpointId = from.EndpointLabel,
        Kind = MessageKind.Request, Route = route, TimeoutMs = 5000
    };

    [Test]
    public void RouteRequest_WhenNoNodeForOriginSession_ShouldThrow()
    {
        // A webview from plugin "A" tries to route, but only plugin "B" has a node registered.
        var bus = new MessageBus();
        var webA = new EndpointId("pluginA", "main", "sA", "web-1", IsNode: false);
        var nodeB = new EndpointId("pluginB", "main", "sB", "node-main", IsNode: true);
        bus.RegisterEndpoint(nodeB, new InMemoryTransport());

        Assert.That(async () => await bus.RouteRequestAsync(Request(webA, "plugin.call.x", "r1"), webA),
            Throws.InvalidOperationException);
    }

    [Test]
    public async Task Event_FromPluginA_ShouldNotBroadcastToPluginB()
    {
        var bus = new MessageBus();
        var nodeA = new EndpointId("pluginA", "main", "sA", "node-main", IsNode: true);
        var webA = new EndpointId("pluginA", "main", "sA", "web-1", IsNode: false);
        var webB = new EndpointId("pluginB", "main", "sB", "web-1", IsNode: false);
        var nodeAT = new InMemoryTransport();
        var webAT = new InMemoryTransport();
        var webBT = new InMemoryTransport();
        bus.RegisterEndpoint(nodeA, nodeAT);
        bus.RegisterEndpoint(webA, webAT);
        bus.RegisterEndpoint(webB, webBT);

        var evt = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "evt", TraceId = "evt", SessionId = "sA",
            PluginId = "pluginA", EntryId = "main", EndpointId = "node-main",
            Kind = MessageKind.Event, Route = "plugin.event.changed"
        };
        nodeAT.Deliver(evt);

        Assert.That(webAT.Sent, Has.Count.EqualTo(1));
        Assert.That(webBT.Sent, Is.Empty);
    }
}
