using System.Threading.Tasks;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Reliability;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Manifest;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Sessions;

[TestFixture]
public class PluginSessionManagerTest
{
    private static PluginManifestV3 Manifest(string pluginId, string nodeEntry = "index.mjs")
        => new()
        {
            Id = pluginId, ProtocolVersion = "3.0",
            Entry = nodeEntry,
            Capabilities = []
        };

    private static RestartPolicy FastRestartPolicy(int maxRestarts) => new(
        baseDelay: TimeSpan.Zero,
        maxDelay: TimeSpan.Zero,
        window: TimeSpan.FromMinutes(5),
        maxRestartsPerWindow: maxRestarts,
        jitter: 0);

    [Test]
    public async Task StartSession_ShouldReachReadyAndRegisterCapabilityManifest()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());
        var manifest = Manifest("settings");

        var session = await mgr.StartSessionAsync(manifest, nodeExePath: "node");

        Assert.That(session.State, Is.EqualTo(SessionState.Ready));
        Assert.That(session.PluginId, Is.EqualTo("settings"));
        Assert.That(session.SessionId, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task StartSession_ShouldAssignUniqueSessionIds()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());

        var s1 = await mgr.StartSessionAsync(Manifest("settings"), "node");
        var s2 = await mgr.StartSessionAsync(Manifest("hello-search"), "node");

        Assert.That(s2.SessionId, Is.Not.EqualTo(s1.SessionId));
    }

    [Test]
    public async Task TryGetSession_AfterStart_ShouldReturnTheSession()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());

        var started = await mgr.StartSessionAsync(Manifest("settings"), "node");
        var found = mgr.TryGetSession("settings", started.SessionId, out var retrieved);

        Assert.That(found, Is.True);
        Assert.That(retrieved!.SessionId, Is.EqualTo(started.SessionId));
    }

    [Test]
    public async Task TryGetSession_WithUnknownSession_ShouldReturnFalse()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());

        var found = mgr.TryGetSession("nope", "never", out var retrieved);

        Assert.That(found, Is.False);
        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public async Task StopSession_ShouldTransitionToStopped()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());
        var session = await mgr.StartSessionAsync(Manifest("settings"), "node");

        await mgr.StopSessionAsync("settings", session.SessionId);

        Assert.That(session.State, Is.EqualTo(SessionState.Stopped));
        Assert.That(mgr.TryGetSession("settings", session.SessionId, out _), Is.False);
    }

    [Test]
    public async Task Disconnect_ShouldRestartWithNewSessionIdAndRaiseSessionReplaced()
    {
        var replaced = new TaskCompletionSource<PluginSessionReplacedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory(),
            restartPolicyFactory: () => FastRestartPolicy(maxRestarts: 5));
        mgr.SessionReplaced += (_, e) => replaced.TrySetResult(e);

        var session = await mgr.StartSessionAsync(Manifest("settings"), "node");
        var oldId = session.SessionId;

        ((InMemoryTransport)session.Controller!.Transport!).Disconnect();

        var args = await replaced.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(args.Previous.SessionId, Is.EqualTo(oldId));
        Assert.That(args.Current.SessionId, Is.Not.EqualTo(oldId));
        Assert.That(args.Current.State, Is.EqualTo(SessionState.Ready));
        Assert.That(mgr.TryGetCurrentSession("settings", out var current), Is.True);
        Assert.That(current!.SessionId, Is.EqualTo(args.Current.SessionId));
    }

    [Test]
    public async Task Disconnect_WhenRestartBudgetExhausted_ShouldStop()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory(),
            restartPolicyFactory: () => FastRestartPolicy(maxRestarts: 0));
        var session = await mgr.StartSessionAsync(Manifest("settings"), "node");

        ((InMemoryTransport)session.Controller!.Transport!).Disconnect();

        Assert.That(await WaitForAsync(() => session.State == SessionState.Stopped), Is.True);
        Assert.That(mgr.TryGetCurrentSession("settings", out _), Is.False);
    }

    [Test]
    public async Task NotifyPeerDead_ShouldRestartLikeDisconnect()
    {
        var replaced = new TaskCompletionSource<PluginSessionReplacedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory(),
            restartPolicyFactory: () => FastRestartPolicy(maxRestarts: 5));
        mgr.SessionReplaced += (_, e) => replaced.TrySetResult(e);

        var session = await mgr.StartSessionAsync(Manifest("settings"), "node");
        var oldId = session.SessionId;

        await mgr.NotifyPeerDeadAsync("settings");

        var args = await replaced.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(args.Previous.SessionId, Is.EqualTo(oldId));
        Assert.That(args.Current.SessionId, Is.Not.EqualTo(oldId));
    }

    [Test]
    public async Task Disconnect_ShouldFailPendingRequestsWithTransportDisconnected()
    {
        var bus = new MessageBus();
        var mgr = new PluginSessionManager(bus, new CapabilityGateway(),
            new FakeProcessControllerFactory(),
            restartPolicyFactory: () => FastRestartPolicy(maxRestarts: 0));
        var session = await mgr.StartSessionAsync(Manifest("settings"), "node");

        var hostT = new InMemoryTransport();
        var hostEp = new EndpointId("settings", session.SessionId, "host", IsNode: false);
        bus.RegisterEndpoint(hostEp, hostT);
        await bus.RouteRequestAsync(new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = "pending-1",
            TraceId = "pending-1",
            SessionId = session.SessionId,
            PluginId = "settings",
            EndpointId = "host",
            Kind = MessageKind.Request,
            Route = "plugin.call.search",
            TimeoutMs = 5000,
        }, hostEp);

        ((InMemoryTransport)session.Controller!.Transport!).Disconnect();

        Assert.That(await WaitForAsync(() => hostT.Sent.Count >= 1), Is.True);
        var fail = hostT.Sent.ToArray()[^1];
        Assert.That(fail.CorrelationId, Is.EqualTo("pending-1"));
        Assert.That(fail.Error?.Code, Is.EqualTo(ErrorCode.TransportDisconnected));
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(20);
        }
        return predicate();
    }
}
