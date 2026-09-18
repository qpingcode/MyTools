using System.Threading.Tasks;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Heartbeat;
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

    private static readonly SessionHeartbeatOptions FastHeartbeat = new(
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromMilliseconds(20),
        2);

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
    public async Task Disconnect_ShouldRaiseSessionUnavailableWithBackendDiagnostics()
    {
        var unavailable = new TaskCompletionSource<PluginSessionUnavailableEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeProcessControllerFactory();
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(), factory,
            restartPolicyFactory: () => FastRestartPolicy(maxRestarts: 0));
        mgr.SessionUnavailable += (_, e) => unavailable.TrySetResult(e);

        var session = await mgr.StartSessionAsync(Manifest("settings"), "node");
        ((FakeProcessController)session.Controller!).FailureDetails = "[stderr] backend exploded";
        ((InMemoryTransport)session.Controller.Transport!).Disconnect();

        var args = await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(args.PluginId, Is.EqualTo("settings"));
        Assert.That(args.SessionId, Is.EqualTo(session.SessionId));
        Assert.That(args.WillRestart, Is.False);
        Assert.That(args.FailureDetails, Is.EqualTo("[stderr] backend exploded"));
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
    public async Task HeartbeatTimeouts_ShouldRestartWithNewSession()
    {
        var replaced = new TaskCompletionSource<PluginSessionReplacedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeProcessControllerFactory();
        var mgr = new PluginSessionManager(
            new MessageBus(),
            new CapabilityGateway(),
            factory,
            restartPolicyFactory: () => FastRestartPolicy(maxRestarts: 1),
            heartbeatOptions: FastHeartbeat);
        mgr.SessionReplaced += (_, e) => replaced.TrySetResult(e);

        var original = await mgr.StartSessionAsync(Manifest("settings"), "node");

        var args = await replaced.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(args.Previous, Is.SameAs(original));
            Assert.That(args.Current.SessionId, Is.Not.EqualTo(original.SessionId));
            Assert.That(args.Current.State, Is.EqualTo(SessionState.Ready));
        });

        await mgr.StopSessionAsync(args.Current.PluginId, args.Current.SessionId);
    }

    [Test]
    public async Task StopSession_ShouldCancelHeartbeatAndCleanControlRoute()
    {
        var bus = new MessageBus();
        var factory = new FakeProcessControllerFactory();
        var options = new SessionHeartbeatOptions(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(1),
            FastHeartbeat.DeadAfter);
        var mgr = new PluginSessionManager(
            bus,
            new CapabilityGateway(),
            factory,
            heartbeatOptions: options);
        var session = await mgr.StartSessionAsync(Manifest("settings"), "node");

        Assert.That(await WaitForAsync(() => bus.ResponseRouteCount == 1), Is.True);
        await mgr.StopSessionAsync(session.PluginId, session.SessionId);

        Assert.Multiple(() =>
        {
            Assert.That(session.HeartbeatLease, Is.Null);
            Assert.That(bus.ResponseRouteCount, Is.Zero);
            Assert.That(session.State, Is.EqualTo(SessionState.Stopped));
        });
    }

    [Test]
    public async Task Disconnect_ShouldClearRoutesWithoutSynthesizingCallerResponses()
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

        await Task.Delay(100);
        Assert.That(hostT.Sent, Is.Empty);
    }

    [Test]
    public async Task ProcessExit_ShouldRecordExitDiagnostics()
    {
        var diagnostics = new PluginDiagnosticsService();
        var factory = new FakeProcessControllerFactory();
        var mgr = new PluginSessionManager(
            new MessageBus(diagnostics: diagnostics),
            new CapabilityGateway(),
            factory,
            diagnostics: diagnostics);
        var session = await mgr.StartSessionAsync(Manifest("settings"), "node");
        var controller = (FakeProcessController)session.Controller!;
        controller.FailureDetails = "[stderr] boom";

        controller.RaiseExited(23);

        var snapshot = diagnostics.GetSnapshot().Plugins.Single(plugin => plugin.PluginId == "settings");
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ProcessExits.Total, Is.EqualTo(1));
            Assert.That(snapshot.LastExitCode, Is.EqualTo(23));
            Assert.That(snapshot.FailureDetails, Is.EqualTo("[stderr] boom"));
        });
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
