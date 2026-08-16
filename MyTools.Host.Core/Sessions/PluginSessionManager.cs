using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Reliability;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Manifest;

namespace MyTools.Host.Core.Sessions;

/// <summary>Raised after an entry's Node session is replaced by a restart (new sessionId).</summary>
public sealed class PluginSessionReplacedEventArgs : EventArgs
{
    public required string PluginId { get; init; }
    public required string EntryId { get; init; }
    public required PluginSession Previous { get; init; }
    public required PluginSession Current { get; init; }
}

/// <summary>
/// Creates, finds, stops and recovers plugin sessions. Each logical entry owns a
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
    private readonly ConcurrentDictionary<string, EntryRuntime> _entries = new();
    private readonly IIdGenerator _ids;
    private readonly BootstrapTokenValidator _tokens;
    private readonly TimeSpan _handshakeTimeout;
    private readonly TimeSpan _tokenTtl;
    private readonly Func<RestartPolicy> _restartPolicyFactory;
    private readonly ILogger _logger;

    public PluginSessionManager(MessageBus bus, CapabilityGateway gateway,
        INodeProcessControllerFactory processFactory, IIdGenerator? ids = null,
        BootstrapTokenValidator? tokens = null,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? tokenTtl = null,
        Func<RestartPolicy>? restartPolicyFactory = null,
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
            maxRestartsPerWindow: 5,
            jitter: 0.2));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Fired after a successful automatic restart replaces the live session for an entry.</summary>
    public event EventHandler<PluginSessionReplacedEventArgs>? SessionReplaced;

    public async Task<PluginSession> StartSessionAsync(PluginManifestV3 manifest, string entryId,
        string nodeExePath, CancellationToken cancellationToken = default)
    {
        var entryKey = EntryKey(manifest.Id, entryId);
        var runtime = _entries.GetOrAdd(entryKey, _ => new EntryRuntime
        {
            Manifest = manifest,
            NodeExePath = nodeExePath,
            ActiveEntryId = entryId,
            Actor = new SessionActor(),
            RestartPolicy = _restartPolicyFactory(),
        });
        runtime.Manifest = manifest;
        runtime.NodeExePath = nodeExePath;
        runtime.ActiveEntryId = entryId;

        return await StartNewSessionForEntryAsync(runtime, entryId, cancellationToken);
    }

    public bool TryGetSession(string pluginId, string entryId, string sessionId, out PluginSession? session)
        => _sessions.TryGetValue(SessionKey(pluginId, entryId, sessionId), out session);

    public bool TryGetCurrentSession(string pluginId, string entryId, out PluginSession? session)
    {
        if (_entries.TryGetValue(EntryKey(pluginId, entryId), out var runtime) && runtime.Session is { } current)
        {
            session = current;
            return true;
        }

        session = null;
        return false;
    }

    public async Task StopSessionAsync(string pluginId, string entryId, string sessionId)
    {
        if (!_sessions.TryGetValue(SessionKey(pluginId, entryId, sessionId), out var session)) return;
        await TearDownSessionAsync(session, markStopped: true, removeEntry: true);
    }

    /// <summary>
    /// Treats the Node peer as dead (e.g. heartbeat watchdog) and runs the same recovery path as a
    /// transport disconnect.
    /// </summary>
    public Task NotifyPeerDeadAsync(string pluginId, string entryId)
    {
        if (!_entries.TryGetValue(EntryKey(pluginId, entryId), out var runtime) || runtime.Session is null)
        {
            return Task.CompletedTask;
        }

        var session = runtime.Session;
        return HandleDisconnectAsync(runtime, session, session.GenerationGuard.Current);
    }

    private async Task TearDownSessionAsync(PluginSession session, bool markStopped, bool removeEntry)
    {
        var pluginId = session.PluginId;
        var entryId = session.EntryId;
        var sessionId = session.SessionId;
        const string endpointId = EndpointIds.NodeMain;

        if (markStopped && session.State is not SessionState.Stopping and not SessionState.Stopped
            and not SessionState.Restarting)
        {
            try { session.Transition(SessionState.Stopping); } catch { /* ignore */ }
        }
        else if (markStopped && session.State is SessionState.Restarting)
        {
            try { session.Transition(SessionState.Stopping); } catch { /* ignore */ }
        }

        _bus.FailPendingForSession(pluginId, entryId, sessionId,
            BusError.For(ErrorCode.TransportDisconnected, "node transport disconnected"));

        var nodeEp = new EndpointId(pluginId, entryId, sessionId, endpointId, IsNode: true);
        _bus.UnregisterEndpoint(nodeEp);

        if (session.Controller?.Transport is { } transport && session.DisconnectHandler is { } handler)
        {
            transport.Disconnected -= handler;
            session.DisconnectHandler = null;
        }

        if (session.Controller is not null)
        {
            try { await session.Controller.StopAsync(); } catch { /* best-effort */ }
        }

        _gateway.UnregisterManifest(pluginId, entryId);
        _sessions.TryRemove(SessionKey(pluginId, entryId, sessionId), out _);

        if (markStopped && session.State is SessionState.Stopping)
        {
            try { session.Transition(SessionState.Stopped); } catch { /* ignore */ }
        }

        if (removeEntry)
        {
            _entries.TryRemove(EntryKey(pluginId, entryId), out _);
        }
    }

    private async Task HandleDisconnectAsync(EntryRuntime runtime, PluginSession session, GenerationToken gen)
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
                "Session disconnect plugin={PluginId} entry={EntryId} session={SessionId} willRestart={WillRestart}",
                session.PluginId, session.EntryId, session.SessionId, shouldRestart);
        });

        if (session.State is not SessionState.Restarting && !shouldRestart)
        {
            // Generation changed or already tearing down.
            return;
        }

        await TearDownSessionAsync(session, markStopped: !shouldRestart, removeEntry: !shouldRestart);

        if (!shouldRestart)
        {
            return;
        }

        runtime.RestartPolicy.RecordRestart();
        try { await Task.Delay(runtime.RestartPolicy.NextDelay()); }
        catch { /* ignore */ }

        try
        {
            var previous = session;
            var neu = await StartNewSessionForEntryAsync(runtime, runtime.ActiveEntryId!, CancellationToken.None);
            _logger.LogWarning(
                "Session restarted plugin={PluginId} entry={EntryId} oldSession={Old} newSession={New}",
                neu.PluginId, neu.EntryId, previous.SessionId, neu.SessionId);
            SessionReplaced?.Invoke(this, new PluginSessionReplacedEventArgs
            {
                PluginId = neu.PluginId,
                EntryId = neu.EntryId,
                Previous = previous,
                Current = neu,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Session restart failed plugin={PluginId} entry={EntryId}",
                runtime.Manifest.Id, runtime.ActiveEntryId);
            await runtime.Actor.PostAsync(() =>
            {
                if (runtime.Session?.State is SessionState.Restarting)
                {
                    try { runtime.Session.Transition(SessionState.Stopping); } catch { }
                    try { runtime.Session.Transition(SessionState.Stopped); } catch { }
                }
            });
            _entries.TryRemove(EntryKey(runtime.Manifest.Id, runtime.ActiveEntryId!), out _);
        }
    }

    private async Task<PluginSession> StartNewSessionForEntryAsync(
        EntryRuntime runtime, string entryId, CancellationToken cancellationToken)
    {
        var manifest = runtime.Manifest;
        var entry = manifest.Entries.First(e => e.Id == entryId);
        var sessionId = _ids.NewId();
        var session = new PluginSession(manifest.Id, entryId, sessionId);
        const string endpointId = EndpointIds.NodeMain;

        session.Transition(SessionState.Starting);

        var pipeName = $"mytools-plugin-{_ids.NewId()}";
        var controller = _processFactory.Create(runtime.NodeExePath, entry.Entry);

        try
        {
            await controller.StartAsync(pipeName, manifest.Id, entryId, identity =>
            {
                var issued = _tokens.Issue(identity, _tokenTtl);
                return issued.Value;
            }, cancellationToken);

            var observed = controller.ObservedIdentity
                ?? throw new InvalidOperationException("process controller did not report identity");

            session.Controller = controller;
            session.Transition(SessionState.Handshaking);

            await PipeHandshake.CompleteAsHostAsync(
                controller.Transport!,
                _tokens,
                observed,
                sessionId,
                endpointId,
                _ids,
                _handshakeTimeout,
                cancellationToken);

            var nodeEp = new EndpointId(manifest.Id, entryId, sessionId, endpointId, IsNode: true);
            // Manifest must be in the gateway before the endpoint is live: settings (and other
            // hostCall clients) send host.call immediately after handshake, on the inbound
            // transport thread.
            _gateway.RegisterManifest(new PluginManifest(manifest.Id, entryId, entry.Capabilities));
            _bus.RegisterEndpoint(nodeEp, controller.Transport!);
            _sessions[SessionKey(manifest.Id, entryId, sessionId)] = session;

            WireDisconnect(runtime, session);

            session.Transition(SessionState.Ready);
            runtime.Session = session;
            runtime.ActiveEntryId = entryId;
            return session;
        }
        catch
        {
            try { await controller.StopAsync(); } catch { /* best-effort */ }
            _gateway.UnregisterManifest(manifest.Id, entryId);
            if (session.Controller is not null)
            {
                var nodeEp = new EndpointId(manifest.Id, entryId, sessionId, endpointId, IsNode: true);
                _bus.UnregisterEndpoint(nodeEp);
            }
            _sessions.TryRemove(SessionKey(manifest.Id, entryId, sessionId), out _);
            if (session.State is not SessionState.Stopped and not SessionState.Created)
            {
                try { session.Transition(SessionState.Stopping); } catch { }
                try { session.Transition(SessionState.Stopped); } catch { }
            }
            throw;
        }
    }

    private void WireDisconnect(EntryRuntime runtime, PluginSession session)
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

    private static string SessionKey(string pluginId, string entryId, string sessionId)
        => $"{pluginId}\u001f{entryId}\u001f{sessionId}";

    private static string EntryKey(string pluginId, string entryId)
        => $"{pluginId}\u001f{entryId}";

    private sealed class EntryRuntime
    {
        public required PluginManifestV3 Manifest { get; set; }
        public required string NodeExePath { get; set; }
        public required string ActiveEntryId { get; set; }
        public required SessionActor Actor { get; init; }
        public required RestartPolicy RestartPolicy { get; init; }
        public PluginSession? Session { get; set; }
    }
}
