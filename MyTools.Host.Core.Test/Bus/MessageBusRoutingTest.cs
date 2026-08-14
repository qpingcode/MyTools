using System.Threading.Tasks;
using System.Linq;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Bus;

[TestFixture]
public class MessageBusRoutingTest
{
    private static EndpointId NodeEp(string plugin, string entry, string session)
        => new(plugin, entry, session, "node-main", IsNode: true);

    private static EndpointId WebEp(string plugin, string entry, string session, string ep)
        => new(plugin, entry, session, ep, IsNode: false);

    private static Envelope Request(EndpointId from, string route, string id) => new()
    {
        Version = ProtocolVersion.Current, Id = id, TraceId = id, SessionId = from.SessionId,
        PluginId = from.PluginId, EntryId = from.EntryId, EndpointId = from.EndpointLabel,
        Kind = MessageKind.Request, Route = route, TimeoutMs = 5000
    };

    private static Envelope Response(EndpointId from, string corrId) => new()
    {
        Version = ProtocolVersion.Current, Id = "resp-" + corrId, CorrelationId = corrId,
        TraceId = corrId, SessionId = from.SessionId, PluginId = from.PluginId,
        EntryId = from.EntryId, EndpointId = from.EndpointLabel,
        Kind = MessageKind.Response, Route = "plugin.call.save"
    };

    [Test]
    public async Task RouteRequest_WebViewToNode_ShouldDeliverToNodeTransport()
    {
        var bus = new MessageBus();
        var webT = new InMemoryTransport();
        var nodeT = new InMemoryTransport();
        var webEp = WebEp("settings", "main", "s1", "web-1");
        var nodeEp = NodeEp("settings", "main", "s1");
        bus.RegisterEndpoint(webEp, webT);
        bus.RegisterEndpoint(nodeEp, nodeT);

        await bus.RouteRequestAsync(Request(webEp, "plugin.call.save", "req-1"), webEp);

        Assert.That(nodeT.Sent, Has.Count.EqualTo(1));
        Assert.That(nodeT.Sent.ToArray()[0].Id, Is.EqualTo("req-1"));
        Assert.That(webT.Sent, Is.Empty);
    }

    [Test]
    public async Task Response_FromNode_ShouldCorrelateBackToOriginatingTransport()
    {
        var bus = new MessageBus();
        var webT = new InMemoryTransport();
        var nodeT = new InMemoryTransport();
        var webEp = WebEp("settings", "main", "s1", "web-1");
        var nodeEp = NodeEp("settings", "main", "s1");
        bus.RegisterEndpoint(webEp, webT);
        bus.RegisterEndpoint(nodeEp, nodeT);

        await bus.RouteRequestAsync(Request(webEp, "plugin.call.save", "req-1"), webEp);
        // Node responds.
        nodeT.Deliver(Response(nodeEp, "req-1"));

        Assert.That(webT.Sent, Has.Count.EqualTo(1));
        Assert.That(webT.Sent.ToArray()[0].CorrelationId, Is.EqualTo("req-1"));
    }

    [Test]
    public async Task Response_WithUnknownCorrelationId_ShouldBeDropped()
    {
        var bus = new MessageBus();
        var webT = new InMemoryTransport();
        var nodeT = new InMemoryTransport();
        var webEp = WebEp("settings", "main", "s1", "web-1");
        var nodeEp = NodeEp("settings", "main", "s1");
        bus.RegisterEndpoint(webEp, webT);
        bus.RegisterEndpoint(nodeEp, nodeT);

        // A response with no matching pending request.
        nodeT.Deliver(Response(nodeEp, "no-such-request"));

        Assert.That(webT.Sent, Is.Empty);
    }
}
