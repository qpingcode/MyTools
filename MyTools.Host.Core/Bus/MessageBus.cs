using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Messages;

namespace MyTools.Host.Core.Bus;

/// <summary>
/// The host-central message bus. Routes requests to the target Node endpoint within a session,
/// correlates responses back to the originating endpoint, and broadcasts events to session
/// endpoints. Isolates traffic by plugin/entry/session (no cross-plugin routing).
///
/// Per the design: the bus does not trust inbound identity fields — transport-bound endpoint
/// identities are used. The bus is fully async and never blocks inside a lock.
///
/// Transport model: each registered transport is the bus's view of one connection (to a Node or a
/// WebView). Writing on transport T delivers to T's remote endpoint; T's MessageReceived carries
/// what that remote endpoint sent to the bus.
/// </summary>
public sealed class MessageBus
{
    private readonly ConcurrentDictionary<string, SessionEndpoints> _sessions = new();
    private readonly ConcurrentDictionary<string, EndpointId> _pending = new();

    public void RegisterEndpoint(EndpointId id, IMessageTransport transport)
    {
        var key = SessionKey(id.PluginId, id.EntryId, id.SessionId);
        var session = _sessions.GetOrAdd(key, _ => new SessionEndpoints());
        session.Add(id, transport);
        transport.MessageReceived += env => OnInbound(id, env);
    }

    public void UnregisterEndpoint(EndpointId id)
    {
        var key = SessionKey(id.PluginId, id.EntryId, id.SessionId);
        if (_sessions.TryGetValue(key, out var session))
        {
            session.Remove(id);
        }
    }

    /// <summary>
    /// Routes a request from <paramref name="origin"/> to the Node endpoint of the same session.
    /// Records the correlation so the eventual response returns to the origin.
    /// </summary>
    public async Task RouteRequestAsync(Envelope request, EndpointId origin)
    {
        var key = SessionKey(origin.PluginId, origin.EntryId, origin.SessionId);
        if (!_sessions.TryGetValue(key, out var session) || session.NodeLabel is null)
        {
            throw new InvalidOperationException($"no node endpoint registered for session {key}");
        }

        _pending[request.Id] = origin;
        // Deliver to the node: write on the node's transport.
        await session.WriteOnAsync(session.NodeLabel, request);
    }

    private void OnInbound(EndpointId source, Envelope env)
    {
        switch (env.Kind)
        {
            case MessageKind.Response:
                HandleResponse(env);
                break;
            case MessageKind.Event:
                BroadcastEvent(source, env);
                break;
            // Requests arriving on a transport (Node calling host.call.*) are handled by the
            // capability gateway in a later test; ignored at the bus level here.
        }
    }

    private void HandleResponse(Envelope env)
    {
        if (env.CorrelationId is null) return;
        if (!_pending.TryRemove(env.CorrelationId, out var origin)) return;

        var key = SessionKey(origin.PluginId, origin.EntryId, origin.SessionId);
        if (!_sessions.TryGetValue(key, out var session)) return;
        // Deliver the response to the originating endpoint.
        session.WriteOn(origin.EndpointLabel, env);
    }

    private void BroadcastEvent(EndpointId source, Envelope env)
    {
        var key = SessionKey(source.PluginId, source.EntryId, source.SessionId);
        if (!_sessions.TryGetValue(key, out var session)) return;
        session.BroadcastExcept(source.EndpointLabel, env);
    }

    /// <summary>
    /// Broadcasts a <c>host.event.*</c> envelope to every endpoint in the target session. The host
    /// is not itself an endpoint, so there is no source to exclude.
    /// </summary>
    public Task BroadcastHostEventAsync(EndpointId anyEndpointInSession, Envelope env)
    {
        var key = SessionKey(anyEndpointInSession.PluginId, anyEndpointInSession.EntryId, anyEndpointInSession.SessionId);
        if (!_sessions.TryGetValue(key, out var session)) return Task.CompletedTask;
        session.BroadcastToAll(env);
        return Task.CompletedTask;
    }

    private static string SessionKey(string pluginId, string entryId, string sessionId)
        => $"{pluginId}\u001f{entryId}\u001f{sessionId}";

    private sealed class SessionEndpoints
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, (EndpointId Id, IMessageTransport Transport)> _byLabel = new();
        public string? NodeLabel { get; private set; }

        public void Add(EndpointId id, IMessageTransport transport)
        {
            lock (_gate)
            {
                _byLabel[id.EndpointLabel] = (id, transport);
                if (id.IsNode) NodeLabel = id.EndpointLabel;
            }
        }

        public void Remove(EndpointId id)
        {
            lock (_gate)
            {
                _byLabel.Remove(id.EndpointLabel);
                if (NodeLabel == id.EndpointLabel) NodeLabel = null;
            }
        }

        public Task WriteOnAsync(string label, Envelope env)
        {
            IMessageTransport? t;
            lock (_gate) t = _byLabel.TryGetValue(label, out var ep) ? ep.Transport : null;
            return t is null ? Task.CompletedTask : t.SendAsync(env, CancellationToken.None).AsTask();
        }

        public void WriteOn(string label, Envelope env)
        {
            lock (_gate)
            {
                if (_byLabel.TryGetValue(label, out var ep))
                {
                    ep.Transport.SendAsync(env, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                }
            }
        }

        public void BroadcastExcept(string sourceLabel, Envelope env)
        {
            lock (_gate)
            {
                foreach (var (label, (_, transport)) in _byLabel)
                {
                    if (label == sourceLabel) continue;
                    transport.SendAsync(env, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                }
            }
        }

        public void BroadcastToAll(Envelope env)
        {
            lock (_gate)
            {
                foreach (var (_, (_, transport)) in _byLabel)
                {
                    transport.SendAsync(env, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                }
            }
        }
    }
}
