using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Manifest;

namespace MyTools.Host.Core.Sessions;

/// <summary>
/// Creates, finds, stops and recovers plugin sessions. Each session is one entry run: a Node
/// endpoint plus zero or more WebView endpoints, all registered on the <see cref="MessageBus"/>.
/// The manager drives the session state machine and bumps the generation on each restart.
///
/// Phase 1: spawn + named-pipe connect + <c>bus.handshake</c> (token + version) before Ready.
/// Restart-on-crash and backoff remain a later wiring step (RestartPolicy / heartbeat).
/// </summary>
public sealed class PluginSessionManager
{
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultTokenTtl = TimeSpan.FromSeconds(30);

    private readonly MessageBus _bus;
    private readonly CapabilityGateway _gateway;
    private readonly INodeProcessControllerFactory _processFactory;
    private readonly ConcurrentDictionary<string, PluginSession> _sessions = new();
    private readonly IIdGenerator _ids;
    private readonly BootstrapTokenValidator _tokens;
    private readonly TimeSpan _handshakeTimeout;
    private readonly TimeSpan _tokenTtl;

    public PluginSessionManager(MessageBus bus, CapabilityGateway gateway,
        INodeProcessControllerFactory processFactory, IIdGenerator? ids = null,
        BootstrapTokenValidator? tokens = null,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? tokenTtl = null)
    {
        _bus = bus;
        _gateway = gateway;
        _processFactory = processFactory;
        _ids = ids ?? new GuidIdGenerator();
        _tokens = tokens ?? new BootstrapTokenValidator();
        _handshakeTimeout = handshakeTimeout ?? DefaultHandshakeTimeout;
        _tokenTtl = tokenTtl ?? DefaultTokenTtl;
    }

    public async Task<PluginSession> StartSessionAsync(PluginManifestV3 manifest, string entryId,
        string nodeExePath, CancellationToken cancellationToken = default)
    {
        var entry = manifest.Entries.First(e => e.EntryId == entryId);
        var sessionId = _ids.NewId();
        var session = new PluginSession(manifest.Id, entryId, sessionId);
        const string endpointId = "node-main";

        session.Transition(SessionState.Starting);

        var pipeName = $"mytools-plugin-{_ids.NewId()}";
        var controller = _processFactory.Create(nodeExePath, entry.NodeEntry);

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

            // Handshake is the only allowed request before the endpoint joins the bus — complete it
            // before RegisterEndpoint so the bus subscriber cannot swallow the buffered handshake.
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
            _bus.RegisterEndpoint(nodeEp, controller.Transport!);
            _gateway.RegisterManifest(new PluginManifest(manifest.Id, entryId, entry.Capabilities));
            _sessions[SessionKey(manifest.Id, entryId, sessionId)] = session;

            session.Transition(SessionState.Ready);
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
                try { session.Transition(SessionState.Stopping); } catch { /* may already be terminal */ }
                try { session.Transition(SessionState.Stopped); } catch { /* ignore */ }
            }
            throw;
        }
    }

    public bool TryGetSession(string pluginId, string entryId, string sessionId, out PluginSession? session)
        => _sessions.TryGetValue(SessionKey(pluginId, entryId, sessionId), out session);

    public async Task StopSessionAsync(string pluginId, string entryId, string sessionId)
    {
        if (!_sessions.TryGetValue(SessionKey(pluginId, entryId, sessionId), out var session)) return;
        session.Transition(SessionState.Stopping);
        var nodeEp = new EndpointId(pluginId, entryId, sessionId, "node-main", IsNode: true);
        _bus.UnregisterEndpoint(nodeEp);
        if (session.Controller is not null)
        {
            await session.Controller.StopAsync();
        }
        _gateway.UnregisterManifest(pluginId, entryId);
        session.Transition(SessionState.Stopped);
        _sessions.TryRemove(SessionKey(pluginId, entryId, sessionId), out _);
    }

    private static string SessionKey(string pluginId, string entryId, string sessionId)
        => $"{pluginId}\u001f{entryId}\u001f{sessionId}";
}
