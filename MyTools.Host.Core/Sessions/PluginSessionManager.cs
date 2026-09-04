using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Reliability;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Manifest;

namespace MyTools.Host.Core.Sessions;

/// <summary>Raised after a plugin's Node session is replaced by a restart (new sessionId).</summary>
public sealed class PluginSessionReplacedEventArgs : EventArgs
{
    public required string PluginId { get; init; }
    public required PluginSession Previous { get; init; }
    public required PluginSession Current { get; init; }
}

/// <summary>Raised as soon as a ready Node session disconnects, before any restart attempt.</summary>
public sealed class PluginSessionUnavailableEventArgs : EventArgs
{
    public required string PluginId { get; init; }
    public required string SessionId { get; init; }
    public string? FailureDetails { get; init; }
}

/// <summary>
/// Creates, finds, stops and recovers plugin sessions. Each plugin owns a
/// <see cref="SessionActor"/> and <see cref="RestartPolicy"/>. On Node disconnect or peer-dead,
/// pending requests fail with <see cref="ErrorCode.TransportDisconnected"/>, the process tree is
/// reclaimed, and a new session is started (new pipe/token/sessionId) while under the restart limit.
/// </summary>
public sealed class PluginSessionManager
{
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultTokenTtl = TimeSpan.FromSeconds(30);

    private readonly MessageBus _bus;
    private readonly CapabilityGateway _gateway;
    private readonly INodeProcessControllerFactory _processFactory;
    private readonly ConcurrentDictionary<string, PluginSession> _sessions = new();
    private readonly ConcurrentDictionary<string, PluginRuntime> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly IIdGenerator _ids;
    private readonly BootstrapTokenValidator _tokens;
    private readonly TimeSpan _handshakeTimeout;
    private readonly TimeSpan _tokenTtl;
    private readonly Func<RestartPolicy> _restartPolicyFactory;
    private readonly ILogger _logger;
    private readonly IPluginDiagnosticsService? _diagnostics;

    public PluginSessionManager(MessageBus bus, CapabilityGateway gateway,
        INodeProcessControllerFactory processFactory, IIdGenerator? ids = null,
        BootstrapTokenValidator? tokens = null,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? tokenTtl = null,
        Func<RestartPolicy>? restartPolicyFactory = null,
        IPluginDiagnosticsService? diagnostics = null,
        ILogger? logger = null)
    {
        _bus = bus;
        _gateway = gateway;
        _processFactory = processFactory;
        _ids = ids ?? new GuidIdGenerator();
        _tokens = tokens ?? new BootstrapTokenValidator();
        _handshakeTimeout = handshakeTimeout ?? DefaultHandshakeTimeout;
        _tokenTtl = tokenTtl ?? DefaultTokenTtl;
        _restartPolicyFactory = restartPolicyFactory ?? (() => new RestartPolicy(
            baseDelay: TimeSpan.FromMilliseconds(200),
            maxDelay: TimeSpan.FromSeconds(5),
            window: TimeSpan.FromMinutes(5),
            maxRestartsPerWindow: 2,
            jitter: 0.2));
        _diagnostics = diagnostics;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Fired after a successful automatic restart replaces the live plugin session.</summary>
    public event EventHandler<PluginSessionReplacedEventArgs>? SessionReplaced;

    /// <summary>Fired when a Node backend disconnects so active detail views can report the failure.</summary>
    public event EventHandler<PluginSessionUnavailableEventArgs>? SessionUnavailable;

    public async Task<PluginSession> StartSessionAsync(PluginManifestV3 manifest,
        string nodeExePath, CancellationToken cancellationToken = default)
    {
        var runtime = _plugins.GetOrAdd(manifest.Id, _ => new PluginRuntime
        {
            Manifest = manifest,
            NodeExePath = nodeExePath,
            Actor = new SessionActor(),
            RestartPolicy = _restartPolicyFactory(),
        });
        runtime.Manifest = manifest;
        runtime.NodeExePath = nodeExePath;

        return await StartNewSessionAsync(runtime, cancellationToken);
    }

    public bool TryGetSession(string pluginId, string sessionId, out PluginSession? session)
        => _sessions.TryGetValue(SessionKey(pluginId, sessionId), out session);

    public bool TryGetCurrentSession(string pluginId, out PluginSession? session)
    {
        if (_plugins.TryGetValue(pluginId, out var runtime) && runtime.Session is { } current)
        {
            session = current;
            return true;
        }

        session = null;
        return false;
    }

    public async Task StopSessionAsync(string pluginId, string sessionId)
    {
        if (!_sessions.TryGetValue(SessionKey(pluginId, sessionId), out var session)) return;
        await TearDownSessionAsync(session, markStopped: true, removePlugin: true);
    }

    /// <summary>
    /// Treats the Node peer as dead (e.g. heartbeat watchdog) and runs the same recovery path as a
    /// transport disconnect.
    /// </summary>
    public Task NotifyPeerDeadAsync(string pluginId)
    {
        if (!_plugins.TryGetValue(pluginId, out var runtime) || runtime.Session is null)
        {
            return Task.CompletedTask;
        }

        var session = runtime.Session;
        return HandleDisconnectAsync(runtime, session, session.GenerationGuard.Current);
    }

    private async Task TearDownSessionAsync(PluginSession session, bool markStopped, bool removePlugin)
    {
        var pluginId = session.PluginId;
        var sessionId = session.SessionId;
        const string endpointId = EndpointIds.NodeMain;

        if (markStopped && session.State is not SessionState.Stopping and not SessionState.Stopped
            and not SessionState.Restarting)
        {
            try { session.Transition(SessionState.Stopping); } catch { /* ignore */ }
            _diagnostics?.RecordSessionState(pluginId, sessionId, SessionState.Stopping, failureDetails: session.Controller?.FailureDetails);
        }
        else if (markStopped && session.State is SessionState.Restarting)
        {
            try { session.Transition(SessionState.Stopping); } catch { /* ignore */ }
            _diagnostics?.RecordSessionState(pluginId, sessionId, SessionState.Stopping, failureDetails: session.Controller?.FailureDetails);
        }

        _bus.FailPendingForSession(pluginId, sessionId,
            BusError.For(ErrorCode.TransportDisconnected, "node transport disconnected"));

        var nodeEp = new EndpointId(pluginId, sessionId, endpointId, IsNode: true);
        _bus.UnregisterEndpoint(nodeEp);

        if (session.Controller?.Transport is { } transport && session.DisconnectHandler is { } handler)
        {
            transport.Disconnected -= handler;
            session.DisconnectHandler = null;
        }

        if (session.Controller is { } controller)
        {
            if (session.ProcessExitHandler is { } processExitHandler)
            {
                controller.ProcessExited -= processExitHandler;
                session.ProcessExitHandler = null;
            }

            _diagnostics?.DetachProcessController(pluginId, sessionId, controller);
        }

        if (session.Controller is not null)
        {
            try { await session.Controller.StopAsync(); } catch { /* best-effort */ }
        }

        _gateway.UnregisterManifest(pluginId);
        _sessions.TryRemove(SessionKey(pluginId, sessionId), out _);

        if (markStopped && session.State is SessionState.Stopping)
        {
            try { session.Transition(SessionState.Stopped); } catch { /* ignore */ }
            _diagnostics?.RecordSessionState(pluginId, sessionId, SessionState.Stopped, failureDetails: session.Controller?.FailureDetails);
        }
        else if (session.State is SessionState.Restarting)
        {
            _diagnostics?.ClearSession(pluginId, sessionId, SessionState.Restarting, session.Controller?.FailureDetails);
        }

        if (removePlugin)
        {
            _plugins.TryRemove(pluginId, out _);
        }
    }

    private async Task HandleDisconnectAsync(PluginRuntime runtime, PluginSession session, GenerationToken gen)
    {
        var shouldRestart = false;
        await runtime.Actor.PostAsync(() =>
        {
            if (!session.GenerationGuard.IsCurrent(gen)) return;
            if (session.State is SessionState.Stopping or SessionState.Stopped or SessionState.Restarting)
            {
                return;
            }

            try { session.Transition(SessionState.Restarting); }
            catch { return; }

            shouldRestart = runtime.RestartPolicy.CanRestart();
            _logger.LogWarning(
                "Session disconnect plugin={PluginId} session={SessionId} willRestart={WillRestart}",
                session.PluginId, session.SessionId, shouldRestart);
            _diagnostics?.RecordDisconnect(session.PluginId, session.SessionId, shouldRestart, session.Controller?.FailureDetails);

            SessionUnavailable?.Invoke(this, new PluginSessionUnavailableEventArgs
            {
                PluginId = session.PluginId,
                SessionId = session.SessionId,
                FailureDetails = session.Controller?.FailureDetails,
            });
        });

        if (session.State is not SessionState.Restarting && !shouldRestart)
        {
            // Generation changed or already tearing down.
            return;
        }

        await TearDownSessionAsync(session, markStopped: !shouldRestart, removePlugin: !shouldRestart);

        if (!shouldRestart)
        {
            _diagnostics?.RecordRestartExhausted(session.PluginId, session.SessionId, session.Controller?.FailureDetails);
            return;
        }

        runtime.RestartPolicy.RecordRestart();
        try { await Task.Delay(runtime.RestartPolicy.NextDelay()); }
        catch { /* ignore */ }

        try
        {
            var previous = session;
            var neu = await StartNewSessionAsync(runtime, CancellationToken.None);
            _logger.LogWarning(
                "Session restarted plugin={PluginId} oldSession={Old} newSession={New}",
                neu.PluginId, previous.SessionId, neu.SessionId);
            _diagnostics?.RecordRestart(neu.PluginId, previous.SessionId, neu.SessionId);
            SessionReplaced?.Invoke(this, new PluginSessionReplacedEventArgs
            {
                PluginId = neu.PluginId,
                Previous = previous,
                Current = neu,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Session restart failed plugin={PluginId}",
                runtime.Manifest.Id);
            _diagnostics?.RecordRestartExhausted(runtime.Manifest.Id, session.SessionId, ex.Message);
            await runtime.Actor.PostAsync(() =>
            {
                if (runtime.Session?.State is SessionState.Restarting)
                {
                    try { runtime.Session.Transition(SessionState.Stopping); } catch { }
                    try { runtime.Session.Transition(SessionState.Stopped); } catch { }
                }
            });
            _plugins.TryRemove(runtime.Manifest.Id, out _);
        }
    }

    private async Task<PluginSession> StartNewSessionAsync(
        PluginRuntime runtime, CancellationToken cancellationToken)
    {
        var manifest = runtime.Manifest;
        var sessionId = _ids.NewId();
        var session = new PluginSession(manifest.Id, sessionId);
        const string endpointId = EndpointIds.NodeMain;

        session.Transition(SessionState.Starting);
        _diagnostics?.RecordSessionState(manifest.Id, sessionId, SessionState.Starting);

        var pipeName = $"mytools-plugin-{_ids.NewId()}";
        var controller = _processFactory.Create(runtime.NodeExePath, manifest.Entry);

        try
        {
            await controller.StartAsync(pipeName, manifest.Id, identity =>
            {
                var issued = _tokens.Issue(identity, _tokenTtl);
                return issued.Value;
            }, cancellationToken);

            var observed = controller.ObservedIdentity
                ?? throw new InvalidOperationException("process controller did not report identity");

            session.Controller = controller;
            _diagnostics?.AttachProcessController(manifest.Id, sessionId, controller);
            WireProcessExit(session);
            session.Transition(SessionState.Handshaking);
            _diagnostics?.RecordSessionState(manifest.Id, sessionId, SessionState.Handshaking, controller.ObservedIdentity?.Pid);

            await PipeHandshake.CompleteAsHostAsync(
                controller.Transport!,
                _tokens,
                observed,
                sessionId,
                endpointId,
                _ids,
                _handshakeTimeout,
                cancellationToken);

            var nodeEp = new EndpointId(manifest.Id, sessionId, endpointId, IsNode: true);
            // Manifest must be in the gateway before the endpoint is live: settings (and other
            // hostCall clients) send host.call immediately after handshake, on the inbound
            // transport thread.
            _gateway.RegisterManifest(new PluginManifest(manifest.Id, manifest.Capabilities));
            _bus.RegisterEndpoint(nodeEp, controller.Transport!);
            _sessions[SessionKey(manifest.Id, sessionId)] = session;

            WireDisconnect(runtime, session);

            session.Transition(SessionState.Ready);
            _diagnostics?.RecordSessionState(manifest.Id, sessionId, SessionState.Ready, controller.ObservedIdentity?.Pid);
            runtime.Session = session;
            return session;
        }
        catch
        {
            try { await controller.StopAsync(); } catch { /* best-effort */ }
            _diagnostics?.DetachProcessController(manifest.Id, sessionId, controller);
            _gateway.UnregisterManifest(manifest.Id);
            if (session.Controller is not null)
            {
                var nodeEp = new EndpointId(manifest.Id, sessionId, endpointId, IsNode: true);
                _bus.UnregisterEndpoint(nodeEp);
            }
            _sessions.TryRemove(SessionKey(manifest.Id, sessionId), out _);
            if (session.State is not SessionState.Stopped and not SessionState.Created)
            {
                try { session.Transition(SessionState.Stopping); } catch { }
                try { session.Transition(SessionState.Stopped); } catch { }
                _diagnostics?.RecordSessionState(manifest.Id, sessionId, SessionState.Stopped, failureDetails: controller.FailureDetails);
            }
            throw;
        }
    }

    private void WireDisconnect(PluginRuntime runtime, PluginSession session)
    {
        var transport = session.Controller?.Transport;
        if (transport is null) return;

        var gen = session.GenerationGuard.Current;
        Action handler = null!;
        handler = () =>
        {
            transport.Disconnected -= handler;
            session.DisconnectHandler = null;
            _ = HandleDisconnectAsync(runtime, session, gen);
        };
        session.DisconnectHandler = handler;
        transport.Disconnected += handler;
    }

    private void WireProcessExit(PluginSession session)
    {
        if (session.Controller is null)
        {
            return;
        }

        Action<NodeProcessExitInfo> handler = info =>
            _diagnostics?.RecordProcessExit(session.PluginId, session.SessionId, info);
        session.ProcessExitHandler = handler;
        session.Controller.ProcessExited += handler;
    }

    private static string SessionKey(string pluginId, string sessionId)
        => $"{pluginId}\u001f{sessionId}";

    private sealed class PluginRuntime
    {
        public required PluginManifestV3 Manifest { get; set; }
        public required string NodeExePath { get; set; }
        public required SessionActor Actor { get; init; }
        public required RestartPolicy RestartPolicy { get; init; }
        public PluginSession? Session { get; set; }
    }
}
