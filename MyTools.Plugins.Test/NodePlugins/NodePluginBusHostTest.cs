using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Plugins.NodePlugins;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Handshake;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

/// <summary>
/// NodePluginBusHost tests: verifies the v3 bus runtime maps INodePluginHost methods to
/// plugin.call.* envelopes and correlates responses via the registered host endpoint — without a
/// second subscription on the Node pipe. host.call.* is authorized by CapabilityGateway.
/// </summary>
[TestFixture]
public class NodePluginBusHostTest
{
    private static NodePluginManifest Manifest() => new()
    {
        Id = "settings",
        NameMessage = new MyTools.Common.Localization.LocalizedMessage("Plugin.Settings.Name", "Settings"),
        Version = "0.0.6",
        Runtime = "node",
        Entry = "backend/index.mjs",
        ProtocolVersion = "3.0",
        PluginDirectory = "C:/fake/settings",
        EntryFullPath = "C:/fake/settings/backend/index.mjs",
        Keywords = ["settings"],
        HotKey = "Alt+S",
        Capabilities = ["configuration.read"],
    };

    [Test]
    public async Task SearchAsync_ShouldSendPluginCallSearchAndCorrelateResponse()
    {
        var (host, nodeT, sessionId) = await CreateStartedHostAsync();

        var searchTask = host.SearchAsync("hello", "global", "en-US", "en-US", "dark", CancellationToken.None);
        await Task.Delay(50);

        var sentRequest = nodeT.Sent.FirstOrDefault(e => e.Route == "plugin.call.search");
        Assert.That(sentRequest, Is.Not.Null);
        Assert.That(sentRequest!.Kind, Is.EqualTo(MessageKind.Request));

        nodeT.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current, Id = "resp-1", CorrelationId = sentRequest.Id,
            TraceId = sentRequest.TraceId, SessionId = sessionId, PluginId = "settings",
            EndpointId = "node-main", Kind = MessageKind.Response,
            Route = "plugin.call.search",
            Payload = JsonNode.Parse("""{"items":[{"id":"1","title":"Hi","subtitle":"","priority":0}]}"""),
        });

        var response = await searchTask;
        Assert.That(response.Items, Has.Count.EqualTo(1));
        Assert.That(response.Items[0].Title, Is.EqualTo("Hi"));
    }

    [Test]
    public async Task SearchAsync_WhenCallerCancels_ShouldFailWithCancelledNotTimeout()
    {
        var (host, nodeT, _) = await CreateStartedHostAsync();
        using var cts = new CancellationTokenSource();

        var searchTask = host.SearchAsync("hello", "global", "en-US", "en-US", "dark", cts.Token);

        Envelope? sentRequest = null;
        for (var i = 0; i < 20 && sentRequest is null; i++)
        {
            await Task.Delay(25);
            sentRequest = nodeT.Sent.FirstOrDefault(e => e.Route == "plugin.call.search");
        }

        Assert.That(sentRequest, Is.Not.Null, "request must be on the wire before cancel");
        cts.Cancel();

        var ex = Assert.ThrowsAsync<BusCallException>(async () => await searchTask);
        Assert.That(ex!.Code, Is.EqualTo(ErrorCode.Cancelled));
        Assert.That(ex.Message, Does.Contain("cancelled").IgnoreCase);
        Assert.That(ex.Message, Does.Not.Contain("timed out"));
    }

    [Test]
    public async Task SearchAsync_WhenNoResponse_ShouldFailWithRequestTimeout()
    {
        var (host, _, _) = await CreateStartedHostAsync();
        host.RequestTimeoutMs = 80;

        var ex = Assert.ThrowsAsync<BusCallException>(async () =>
            await host.SearchAsync("hello", "global", "en-US", "en-US", "dark", CancellationToken.None));
        Assert.That(ex!.Code, Is.EqualTo(ErrorCode.RequestTimeout));
        Assert.That(ex.Message, Does.Contain("timed out"));
    }

    [Test]
    public async Task EventReceived_ShouldFireWhenPluginPublishesAnEvent()
    {
        var (host, nodeT, sessionId) = await CreateStartedHostAsync();

        NodePluginEventReceivedEventArgs? received = null;
        host.EventReceived += (_, e) => received = e;

        nodeT.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current, Id = "evt-1", TraceId = "evt-1", SessionId = sessionId,
            PluginId = "settings", EndpointId = "node-main",
            Kind = MessageKind.Event, Route = "plugin.event.configChanged",
            Payload = JsonNode.Parse("""{"key":"theme"}"""),
        });
        await Task.Delay(50);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.SubjectId, Is.EqualTo("plugin.event.configChanged"));
    }

    [Test]
    public async Task HostCall_ShouldAuthorizeAndReplyViaBus()
    {
        var (host, nodeT, sessionId) = await CreateStartedHostAsync();

        host.HostCallHandler = (_, _) =>
            Task.FromResult(JsonSerializer.SerializeToElement(new { theme = "light" }));

        nodeT.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current, Id = "hc-1", TraceId = "hc-1", SessionId = sessionId,
            PluginId = "forged", EndpointId = "forged",
            Kind = MessageKind.Request, Route = "host.call.configuration.read", TimeoutMs = 5000,
            Payload = JsonNode.Parse("{}"),
        });

        Envelope? reply = null;
        for (var i = 0; i < 20 && reply is null; i++)
        {
            await Task.Delay(25);
            reply = nodeT.Sent.FirstOrDefault(e =>
                e.Kind == MessageKind.Response && e.CorrelationId == "hc-1");
        }

        Assert.That(reply, Is.Not.Null, "host must reply to host.call on the node transport");
        Assert.That(reply!.Route, Is.EqualTo("host.call.configuration.read"));
        Assert.That(reply.Error, Is.Null);
        Assert.That(reply.PluginId, Is.EqualTo("settings"), "inbound identity must be stamped");
        Assert.That(reply.Payload?.ToJsonString(), Does.Contain("light"));
    }

    [Test]
    public async Task HostCall_WhenCapabilityNotDeclared_ShouldReturnCapabilityNotDeclared()
    {
        var (host, nodeT, sessionId) = await CreateStartedHostAsync(capabilities: []);

        host.HostCallHandler = (_, _) =>
            Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));

        nodeT.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current, Id = "hc-deny", TraceId = "hc-deny", SessionId = sessionId,
            PluginId = "settings", EndpointId = "node-main",
            Kind = MessageKind.Request, Route = "host.call.configuration.read", TimeoutMs = 5000,
            Payload = JsonNode.Parse("{}"),
        });

        Envelope? reply = null;
        for (var i = 0; i < 20 && reply is null; i++)
        {
            await Task.Delay(25);
            reply = nodeT.Sent.FirstOrDefault(e =>
                e.Kind == MessageKind.Response && e.CorrelationId == "hc-deny");
        }

        Assert.That(reply, Is.Not.Null);
        Assert.That(reply!.Error, Is.Not.Null);
        Assert.That(reply.Error!.Code, Is.EqualTo(ErrorCode.CapabilityNotDeclared));
    }

    [Test]
    public async Task DisposeAsync_ShouldStopSessionBeforeCompleting()
    {
        var gateway = new CapabilityGateway();
        var bus = new MessageBus(gateway);
        var factory = new FakeFactory();
        var manager = new PluginSessionManager(bus, gateway, factory);
        var host = new NodePluginBusHost(Manifest(), manager, bus, NullLogger<NodePluginBusHost>.Instance);
        await host.StartAsync("node", CancellationToken.None);
        var sessionId = host.SessionId!;

        await host.DisposeAsync();

        Assert.That(manager.TryGetSession("settings", sessionId, out _), Is.False);
        Assert.That(async () => await host.StartAsync("node", CancellationToken.None),
            Throws.InstanceOf<ObjectDisposedException>());
    }

    private static async Task<(NodePluginBusHost host, InMemoryTransport nodeT, string sessionId)>
        CreateStartedHostAsync(IReadOnlyList<string>? capabilities = null)
    {
        var gateway = new CapabilityGateway();
        var bus = new MessageBus(gateway);
        var factory = new FakeFactory();
        var manager = new PluginSessionManager(bus, gateway, factory);
        var m = new NodePluginManifest
        {
            Id = "settings",
            NameMessage = new MyTools.Common.Localization.LocalizedMessage("Plugin.Settings.Name", "Settings"),
            Version = "0.0.6",
            Runtime = "node",
            Entry = "backend/index.mjs",
            ProtocolVersion = "3.0",
            PluginDirectory = "C:/fake/settings",
            EntryFullPath = "C:/fake/settings/backend/index.mjs",
            Keywords = ["settings"],
            HotKey = "Alt+S",
            Capabilities = capabilities ?? ["configuration.read"],
        };
        var host = new NodePluginBusHost(m, manager, bus, NullLogger<NodePluginBusHost>.Instance);

        await host.StartAsync(nodeExePath: "node", CancellationToken.None);

        var session = host.Session!;
        var nodeT = (InMemoryTransport)factory.LastController!.Transport!;
        return (host, nodeT, session.SessionId);
    }

    private sealed class FakeController : INodeProcessController
    {
        public IMessageTransport? Transport { get; private set; }
        public ProcessIdentity? ObservedIdentity { get; private set; }

        public Task StartAsync(
            string pipeName,
            string pluginId,
            Func<ProcessIdentity, string> issueToken,
            CancellationToken ct)
        {
            var transport = new InMemoryTransport();
            Transport = transport;
            ObservedIdentity = new ProcessIdentity(7, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                pluginId);
            var token = issueToken(ObservedIdentity);
            var payload = HandshakePayload.BuildNamedPipeRequest(PipeHandshake.HostSupportedVersions, token);
            transport.Deliver(new Envelope
            {
                Version = ProtocolVersion.Current,
                Id = "hs-1",
                TraceId = "hs-1",
                SessionId = "",
                PluginId = pluginId,
                EndpointId = "node-main",
                Kind = MessageKind.Request,
                Route = "bus.handshake",
                TimeoutMs = 5000,
                Payload = JsonSerializer.SerializeToNode(payload, ProtocolJsonOptions.Default),
            });
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class FakeFactory : INodeProcessControllerFactory
    {
        public FakeController? LastController { get; private set; }
        public INodeProcessController Create(string nodeExePath, string nodeEntryFullPath)
            => LastController = new FakeController();
    }
}
