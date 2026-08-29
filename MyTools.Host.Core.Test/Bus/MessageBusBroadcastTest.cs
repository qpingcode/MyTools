using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Bus;

[TestFixture]
public class MessageBusBroadcastTest
{
    [Test]
    public void PluginEvent_ShouldBroadcastToAllWebviewsInTheSameSession()
    {
        var bus = new MessageBus();
        var node = new EndpointId("settings", "s1", "node-main", IsNode: true);
        var web1 = new EndpointId("settings", "s1", "web-1", IsNode: false);
        var web2 = new EndpointId("settings", "s1", "web-2", IsNode: false);
        var nodeT = new InMemoryTransport();
        var web1T = new InMemoryTransport();
        var web2T = new InMemoryTransport();
        bus.RegisterEndpoint(node, nodeT);
        bus.RegisterEndpoint(web1, web1T);
        bus.RegisterEndpoint(web2, web2T);

        var evt = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "evt", TraceId = "evt", SessionId = "s1",
            PluginId = "settings", EndpointId = "node-main",
            Kind = MessageKind.Event, Route = "plugin.event.configChanged"
        };
        nodeT.Deliver(evt);

        Assert.That(web1T.Sent, Has.Count.EqualTo(1));
        Assert.That(web2T.Sent, Has.Count.EqualTo(1));
        Assert.That(nodeT.Sent, Is.Empty); // source does not receive its own event
    }

    [Test]
    public void PluginEvent_FromWebview_ShouldBroadcastToOtherWebviewsButNotSource()
    {
        var bus = new MessageBus();
        var node = new EndpointId("settings", "s1", "node-main", IsNode: true);
        var web1 = new EndpointId("settings", "s1", "web-1", IsNode: false);
        var web2 = new EndpointId("settings", "s1", "web-2", IsNode: false);
        var nodeT = new InMemoryTransport();
        var web1T = new InMemoryTransport();
        var web2T = new InMemoryTransport();
        bus.RegisterEndpoint(node, nodeT);
        bus.RegisterEndpoint(web1, web1T);
        bus.RegisterEndpoint(web2, web2T);

        var evt = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "evt", TraceId = "evt", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = MessageKind.Event, Route = "plugin.event.configChanged"
        };
        web1T.Deliver(evt);

        Assert.That(web2T.Sent, Has.Count.EqualTo(1));
        Assert.That(web1T.Sent, Is.Empty); // source excluded
    }

    [Test]
    public void HostEvent_ShouldBroadcastToAllEndpointsInTheTargetSession()
    {
        // host.event.* targets a session and fans out to all endpoints (node + webviews).
        var bus = new MessageBus();
        var node = new EndpointId("settings", "s1", "node-main", IsNode: true);
        var web1 = new EndpointId("settings", "s1", "web-1", IsNode: false);
        var nodeT = new InMemoryTransport();
        var web1T = new InMemoryTransport();
        bus.RegisterEndpoint(node, nodeT);
        bus.RegisterEndpoint(web1, web1T);

        // Host emits an event into the session via a dedicated bus method.
        var evt = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "hevt", TraceId = "hevt", SessionId = "s1",
            PluginId = "settings", EndpointId = "host",
            Kind = MessageKind.Event, Route = "host.event.themeChanged"
        };
        bus.BroadcastHostEventAsync(node with { }, evt).Wait();

        Assert.That(web1T.Sent, Has.Count.EqualTo(1));
        Assert.That(nodeT.Sent, Has.Count.EqualTo(1));
    }
}
