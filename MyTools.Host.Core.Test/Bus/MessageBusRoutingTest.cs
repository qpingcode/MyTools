using System.Threading.Tasks;
using System.Linq;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Bus;

[TestFixture]
public class MessageBusRoutingTest
{
    private static EndpointId NodeEp(string plugin, string session)
        => new(plugin, session, "node-main", IsNode: true);

    private static EndpointId WebEp(string plugin, string session, string ep)
        => new(plugin, session, ep, IsNode: false);

    private static Envelope Request(EndpointId from, string route, string id) => new()
    {
        Version = ProtocolVersion.Current, Id = id, TraceId = id, SessionId = from.SessionId,
        PluginId = from.PluginId, EndpointId = from.EndpointLabel,
        Kind = MessageKind.Request, Route = route, TimeoutMs = 5000
    };

    private static Envelope Response(EndpointId from, string corrId) => new()
    {
        Version = ProtocolVersion.Current, Id = "resp-" + corrId, CorrelationId = corrId,
        TraceId = corrId, SessionId = from.SessionId, PluginId = from.PluginId,
        EndpointId = from.EndpointLabel,
        Kind = MessageKind.Response, Route = "plugin.call.save"
    };

    [Test]
    public async Task RouteRequest_WebViewToNode_ShouldDeliverToNodeTransport()
    {
        var bus = new MessageBus();
        var webT = new InMemoryTransport();
        var nodeT = new InMemoryTransport();
        var webEp = WebEp("settings", "s1", "web-1");
        var nodeEp = NodeEp("settings", "s1");
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
        var webEp = WebEp("settings", "s1", "web-1");
        var nodeEp = NodeEp("settings", "s1");
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
        var webEp = WebEp("settings", "s1", "web-1");
        var nodeEp = NodeEp("settings", "s1");
        bus.RegisterEndpoint(webEp, webT);
        bus.RegisterEndpoint(nodeEp, nodeT);

        // A response with no matching pending request.
        nodeT.Deliver(Response(nodeEp, "no-such-request"));

        Assert.That(webT.Sent, Is.Empty);
    }

    [Test]
    public void InboundRequest_WhenNodeWasRemoved_ShouldReplyWithTransportDisconnected()
    {
        var bus = new MessageBus();
        var webT = new InMemoryTransport();
        var webEp = WebEp("settings", "s1", "web-1");
        bus.RegisterEndpoint(webEp, webT);

        webT.Deliver(Request(webEp, "plugin.call.save", "req-1"));

        Assert.That(() => webT.Sent.Count, Is.EqualTo(1).After(1000, 10));
        var reply = webT.Sent.ToArray()[0];
        Assert.That(reply.CorrelationId, Is.EqualTo("req-1"));
        Assert.That(reply.Error?.Code, Is.EqualTo(ErrorCode.TransportDisconnected));
    }

    [Test]
    public async Task RouteRequest_DuplicateId_ShouldRejectWithoutReplacingOriginalRoute()
    {
        var bus = new MessageBus();
        var firstWebTransport = new InMemoryTransport();
        var secondWebTransport = new InMemoryTransport();
        var nodeTransport = new InMemoryTransport();
        var firstWeb = WebEp("settings", "s1", "web-1");
        var secondWeb = WebEp("settings", "s1", "web-2");
        var node = NodeEp("settings", "s1");
        bus.RegisterEndpoint(firstWeb, firstWebTransport);
        bus.RegisterEndpoint(secondWeb, secondWebTransport);
        bus.RegisterEndpoint(node, nodeTransport);

        await bus.RouteRequestToNodeAsync(Request(firstWeb, "plugin.call.save", "duplicate"), firstWeb);
        var exception = Assert.ThrowsAsync<MessageRouteException>(async () =>
            await bus.RouteRequestToNodeAsync(
                Request(secondWeb, "plugin.call.save", "duplicate"),
                secondWeb));
        nodeTransport.Deliver(Response(node, "duplicate"));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Error.Code, Is.EqualTo(ErrorCode.InvalidPayload));
            Assert.That(firstWebTransport.Sent, Has.Count.EqualTo(1));
            Assert.That(secondWebTransport.Sent, Is.Empty);
        });
    }

    [Test]
    public void RouteRequest_SendFailure_ShouldRollbackResponseRoute()
    {
        var bus = new MessageBus();
        var webTransport = new InMemoryTransport();
        var failedNodeTransport = new InMemoryTransport();
        var web = WebEp("settings", "s1", "web-1");
        var node = NodeEp("settings", "s1");
        bus.RegisterEndpoint(web, webTransport);
        bus.RegisterEndpoint(node, failedNodeTransport);
        failedNodeTransport.Disconnect();

        var exception = Assert.ThrowsAsync<MessageRouteException>(async () =>
            await bus.RouteRequestToNodeAsync(Request(web, "plugin.call.save", "retryable"), web));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Error.Code, Is.EqualTo(ErrorCode.TransportDisconnected));
            Assert.That(bus.ResponseRouteCount, Is.Zero);
        });
    }

    [Test]
    public async Task AbandonResponseRoute_LateResponse_ShouldBeDropped()
    {
        var bus = new MessageBus();
        var webTransport = new InMemoryTransport();
        var nodeTransport = new InMemoryTransport();
        var web = WebEp("settings", "s1", "web-1");
        var node = NodeEp("settings", "s1");
        bus.RegisterEndpoint(web, webTransport);
        bus.RegisterEndpoint(node, nodeTransport);
        await bus.RouteRequestToNodeAsync(Request(web, "plugin.call.save", "abandoned"), web);

        var abandoned = bus.AbandonResponseRoute("abandoned", web);
        nodeTransport.Deliver(Response(node, "abandoned"));

        Assert.Multiple(() =>
        {
            Assert.That(abandoned, Is.True);
            Assert.That(bus.ResponseRouteCount, Is.Zero);
            Assert.That(webTransport.Sent, Is.Empty);
        });
    }
}
