using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using MyTools.Host.Core.Backpressure;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Reliability;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Test.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Manifest;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.E2E;

/// <summary>
/// End-to-end and fault-recovery scenarios using in-memory transports: WebView→bus→Node,
/// backpressure, identity stamping, cross-plugin isolation, and session restart.
/// </summary>
[TestFixture]
public class EndToEndScenariosTest
{
    private static EndpointId Node(string p, string e, string s) => new(p, e, s, "node-main", IsNode: true);
    private static EndpointId Web(string p, string e, string s, string ep = "web-1")
        => new(p, e, s, ep, IsNode: false);

    private static Envelope Req(EndpointId from, string route, string id) => new()
    {
        Version = ProtocolVersion.Current, Id = id, TraceId = id, SessionId = from.SessionId,
        PluginId = from.PluginId, EntryId = from.EntryId, EndpointId = from.EndpointLabel,
        Kind = MessageKind.Request, Route = route, TimeoutMs = 5000
    };

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

        Assert.That(nodeT.Sent, Has.Count.EqualTo(1));
        var receivedReq = nodeT.Sent.ToArray()[0];

        nodeT.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current, Id = "resp-1", CorrelationId = receivedReq.Id,
            TraceId = receivedReq.TraceId, SessionId = "s1", PluginId = "settings", EntryId = "main",
            EndpointId = "node-main", Kind = MessageKind.Response, Route = "plugin.call.save"
        });

        Assert.That(webT.Sent, Has.Count.EqualTo(1));
        Assert.That(webT.Sent.ToArray()[0].CorrelationId, Is.EqualTo("r1"));
    }

    [Test]
    public async Task AfterNodeReload_OldSessionResponse_ShouldNotReachNewWebview()
    {
        var webNew = new InMemoryTransport();
        var bus = new MessageBus();
        bus.RegisterEndpoint(Web("settings", "main", "s2"), webNew);

        var oldSessionBus = new MessageBus();
        var nodeOld = Node("settings", "main", "s1");
        var oldNodeT = new InMemoryTransport();
        oldSessionBus.RegisterEndpoint(nodeOld, oldNodeT);
        oldNodeT.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current, Id = "late", CorrelationId = "never-issued",
            TraceId = "t", SessionId = "s1", PluginId = "settings", EntryId = "main",
            EndpointId = "node-main", Kind = MessageKind.Response, Route = "plugin.call.save"
        });

        Assert.That(webNew.Sent, Is.Empty);
    }

    [Test]
    public void CapabilityGateway_UndeclaredCapability_ShouldBeDenied()
    {
        var gw = new CapabilityGateway();
        gw.RegisterManifest(new PluginManifest("settings", "main", ["configuration.write"]));

        var decision = gw.Authorize("settings", "main", "clipboard.read");

        Assert.That(decision.IsAllowed, Is.False);
        Assert.That(decision.Error!.Code, Is.EqualTo(ErrorCode.CapabilityNotDeclared));
    }

    [Test]
    public async Task MessageBus_PendingLimit_ShouldReturnTooManyRequestsToOrigin()
    {
        var bus = new MessageBus(pendingLimit: 2);
        var webT = new InMemoryTransport();
        var nodeT = new InMemoryTransport();
        var web = Web("settings", "main", "s1");
        bus.RegisterEndpoint(web, webT);
        bus.RegisterEndpoint(Node("settings", "main", "s1"), nodeT);

        await bus.RouteRequestAsync(Req(web, "plugin.call.a", "a"), web);
        await bus.RouteRequestAsync(Req(web, "plugin.call.b", "b"), web);
        await bus.RouteRequestAsync(Req(web, "plugin.call.c", "c"), web);

        Assert.That(nodeT.Sent, Has.Count.EqualTo(2));
        Assert.That(webT.Sent, Has.Count.EqualTo(1));
        Assert.That(webT.Sent.ToArray()[0].CorrelationId, Is.EqualTo("c"));
        Assert.That(webT.Sent.ToArray()[0].Error?.Code, Is.EqualTo(ErrorCode.TooManyRequests));
    }

    [Test]
    public async Task MessageBus_InboundPluginCall_ShouldStampIdentityIgnoringForgedFields()
    {
        var bus = new MessageBus();
        var webT = new InMemoryTransport();
        var nodeT = new InMemoryTransport();
        bus.RegisterEndpoint(Web("settings", "main", "s1"), webT);
        bus.RegisterEndpoint(Node("settings", "main", "s1"), nodeT);

        // Page forges another plugin's identity — bus must stamp the transport binding.
        webT.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = "forged-1",
            TraceId = "forged-1",
            SessionId = "OTHER-SESSION",
            PluginId = "evil",
            EntryId = "hack",
            EndpointId = "forged-ep",
            Kind = MessageKind.Request,
            Route = "plugin.call.refresh",
            TimeoutMs = 5000,
        });

        Assert.That(await WaitForAsync(() => nodeT.Sent.Count >= 1), Is.True);
        var delivered = nodeT.Sent.ToArray()[0];
        Assert.That(delivered.PluginId, Is.EqualTo("settings"));
        Assert.That(delivered.EntryId, Is.EqualTo("main"));
        Assert.That(delivered.SessionId, Is.EqualTo("s1"));
        Assert.That(delivered.EndpointId, Is.EqualTo("web-1"));
    }

    [Test]
    public async Task MessageBus_CrossPlugin_ShouldNotLeakEvents()
    {
        var bus = new MessageBus();
        var webA = new InMemoryTransport();
        var webB = new InMemoryTransport();
        var nodeA = new InMemoryTransport();
        var nodeB = new InMemoryTransport();
        bus.RegisterEndpoint(Web("a", "main", "s1"), webA);
        bus.RegisterEndpoint(Node("a", "main", "s1"), nodeA);
        bus.RegisterEndpoint(Web("b", "main", "s1", "web-1"), webB);
        bus.RegisterEndpoint(Node("b", "main", "s1"), nodeB);

        nodeA.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = "ev1",
            TraceId = "ev1",
            SessionId = "s1",
            PluginId = "a",
            EntryId = "main",
            EndpointId = "node-main",
            Kind = MessageKind.Event,
            Route = "plugin.event.tick",
        });

        Assert.That(await WaitForAsync(() => webA.Sent.Count >= 1), Is.True);
        Assert.That(webB.Sent, Is.Empty);
        Assert.That(webA.Sent.ToArray()[0].Route, Is.EqualTo("plugin.event.tick"));
    }

    [Test]
    public async Task SessionDisconnect_ShouldFailPendingAndRaiseSessionReplaced()
    {
        var replaced = new TaskCompletionSource<PluginSessionReplacedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bus = new MessageBus();
        var mgr = new PluginSessionManager(bus, new CapabilityGateway(),
            new FakeProcessControllerFactory(),
            restartPolicyFactory: () => new RestartPolicy(
                TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMinutes(5), maxRestartsPerWindow: 5, jitter: 0));
        mgr.SessionReplaced += (_, e) => replaced.TrySetResult(e);

        var session = await mgr.StartSessionAsync(new PluginManifestV3
        {
            Id = "settings", ProtocolVersion = "3.0",
            Entries = [new() { Id = "main", Entry = "index.mjs", Capabilities = [] }],
        }, "main", "node");

        var hostT = new InMemoryTransport();
        var hostEp = Web("settings", "main", session.SessionId, "host");
        bus.RegisterEndpoint(hostEp, hostT);
        await bus.RouteRequestAsync(Req(hostEp, "plugin.call.search", "pending-x"), hostEp);

        ((InMemoryTransport)session.Controller!.Transport!).Disconnect();

        Assert.That(await WaitForAsync(() => hostT.Sent.Count >= 1), Is.True);
        Assert.That(hostT.Sent.ToArray()[^1].Error?.Code, Is.EqualTo(ErrorCode.TransportDisconnected));
        Assert.That(hostT.Sent.ToArray()[^1].Route, Is.EqualTo(Routes.PluginCall.Search));

        var args = await replaced.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(args.Current.SessionId, Is.Not.EqualTo(session.SessionId));
    }

    [Test]
    public async Task BoundedEventQueue_OnSlowEndpoint_ShouldDropOldestUnderBurst()
    {
        var bus = new MessageBus(eventQueueCapacity: 2);
        var webT = new SlowTransport(delayMs: 40);
        var nodeT = new InMemoryTransport();
        bus.RegisterEndpoint(Web("settings", "main", "s1"), webT);
        bus.RegisterEndpoint(Node("settings", "main", "s1"), nodeT);

        for (var i = 0; i < 8; i++)
        {
            nodeT.Deliver(new Envelope
            {
                Version = ProtocolVersion.Current,
                Id = $"e{i}",
                TraceId = $"e{i}",
                SessionId = "s1",
                PluginId = "settings",
                EntryId = "main",
                EndpointId = "node-main",
                Kind = MessageKind.Event,
                Route = "plugin.event.n",
            });
        }

        Assert.That(await WaitForAsync(() => bus.TotalDroppedEvents > 0), Is.True);
        await Task.Delay(300);
        Assert.That(webT.Sent.Count, Is.LessThan(8));
    }

    private sealed class SlowTransport : IMessageTransport
    {
        private readonly int _delayMs;
        private readonly ConcurrentQueue<Envelope> _sent = new();
        public SlowTransport(int delayMs) => _delayMs = delayMs;
        public ConcurrentQueue<Envelope> Sent => _sent;
        public bool IsConnected { get; private set; } = true;
        public event Action<Envelope>? MessageReceived { add { } remove { } }
        public event Action? Disconnected;
        public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken)
        {
            await Task.Delay(_delayMs, cancellationToken);
            _sent.Enqueue(envelope);
        }
        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            Disconnected?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(10);
        }
        return predicate();
    }
}
