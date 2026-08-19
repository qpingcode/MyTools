using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Host.Core.Backpressure;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Host.Core.Bus;

/// <summary>
/// Host-call handler registered per plugin entry. Invoked after <see cref="CapabilityGateway"/>
/// authorizes the route. Returns a JSON payload on success or throws on failure.
/// </summary>
public delegate Task<JsonElement> HostCallInvoker(
    string method, JsonElement parameters, CancellationToken cancellationToken);

/// <summary>
/// The host-central message bus. Routes requests to the target Node endpoint within a session,
/// correlates responses back to the originating endpoint, broadcasts events, and dispatches
/// Node-originated <c>host.call.*</c> through <see cref="CapabilityGateway"/> then the registered
/// <see cref="HostCallInvoker"/>.
///
/// Inbound identity fields are stamped from the transport-bound <see cref="EndpointId"/>; the bus
/// never trusts peer-declared plugin/entry/session/endpoint ids. Per-endpoint
/// <see cref="PendingRequestTracker"/> and <see cref="BoundedEventQueue{T}"/> enforce Phase-1
/// backpressure.
/// </summary>
public sealed class MessageBus
{
    public const int DefaultPendingLimit = 64;
    public const int DefaultEventQueueCapacity = 64;

    private readonly ConcurrentDictionary<string, SessionEndpoints> _sessions = new();
    private readonly ConcurrentDictionary<string, PendingOrigin> _pending = new();
    private readonly ConcurrentDictionary<string, HostCallInvoker> _hostCallHandlers = new();
    private readonly CapabilityGateway _gateway;
    private readonly IIdGenerator _ids;
    private readonly ILogger _logger;
    private readonly int _pendingLimit;
    private readonly int _eventQueueCapacity;

    public MessageBus(
        CapabilityGateway? gateway = null,
        IIdGenerator? ids = null,
        ILogger? logger = null,
        int pendingLimit = DefaultPendingLimit,
        int eventQueueCapacity = DefaultEventQueueCapacity)
    {
        _gateway = gateway ?? new CapabilityGateway();
        _ids = ids ?? new GuidIdGenerator();
        _logger = logger ?? NullLogger.Instance;
        _pendingLimit = pendingLimit;
        _eventQueueCapacity = eventQueueCapacity;
    }

    /// <summary>Sum of dropped outbound events across all registered endpoints (diagnostics).</summary>
    public long TotalDroppedEvents
    {
        get
        {
            long total = 0;
            foreach (var session in _sessions.Values)
            {
                total += session.DroppedEvents;
            }
            return total;
        }
    }

    public void RegisterEndpoint(EndpointId id, IMessageTransport transport)
    {
        var key = SessionKey(id.PluginId, id.EntryId, id.SessionId);
        var session = _sessions.GetOrAdd(key, _ => new SessionEndpoints(_pendingLimit, _eventQueueCapacity));
        Action<Envelope> handler = env => OnInbound(id, env);
        session.Add(id, transport, handler);
        transport.MessageReceived += handler;
        _logger.LogInformation(
            "Bus endpoint registered plugin={PluginId} entry={EntryId} session={SessionId} ep={Endpoint} isNode={IsNode}",
            id.PluginId, id.EntryId, id.SessionId, id.EndpointLabel, id.IsNode);
    }

    public void UnregisterEndpoint(EndpointId id)
    {
        var key = SessionKey(id.PluginId, id.EntryId, id.SessionId);
        if (_sessions.TryGetValue(key, out var session)
            && session.TryRemove(id, out var transport, out var handler))
        {
            transport.MessageReceived -= handler;
            _logger.LogInformation(
                "Bus endpoint unregistered plugin={PluginId} entry={EntryId} session={SessionId} ep={Endpoint}",
                id.PluginId, id.EntryId, id.SessionId, id.EndpointLabel);
        }
    }

    public void RegisterHostCallHandler(string pluginId, string entryId, HostCallInvoker handler)
        => _hostCallHandlers[HandlerKey(pluginId, entryId)] = handler;

    public void UnregisterHostCallHandler(string pluginId, string entryId)
        => _hostCallHandlers.TryRemove(HandlerKey(pluginId, entryId), out _);

    /// <summary>
    /// Routes a request from <paramref name="origin"/> to the Node endpoint of the same session.
    /// Records the correlation so the eventual response returns to the origin. Rejects with
    /// <see cref="ErrorCode.TooManyRequests"/> when the origin's pending cap is reached.
    /// </summary>
    public async Task RouteRequestAsync(Envelope request, EndpointId origin)
    {
        var key = SessionKey(origin.PluginId, origin.EntryId, origin.SessionId);
        if (!_sessions.TryGetValue(key, out var session) || session.NodeLabel is null)
        {
            throw new InvalidOperationException($"no node endpoint registered for session {key}");
        }

        if (!session.TryReserve(origin.EndpointLabel, request.Id, request.Route))
        {
            _logger.LogWarning(
                "TooManyRequests origin={Endpoint} route={Route} traceId={TraceId} inFlightCap={Cap}",
                origin.EndpointLabel, request.Route, request.TraceId, _pendingLimit);
            await session.WriteOnAsync(origin.EndpointLabel, BuildErrorReply(request, origin,
                BusError.For(ErrorCode.TooManyRequests,
                    $"pending request limit {_pendingLimit} reached for endpoint {origin.EndpointLabel}",
                    retryable: true)));
            return;
        }

        _pending[request.Id] = new PendingOrigin(origin, request.Route, Stopwatch.GetTimestamp());
        _logger.LogDebug(
            "RouteRequest id={Id} traceId={TraceId} route={Route} origin={Origin} -> node",
            request.Id, request.TraceId, request.Route, origin.EndpointLabel);
        await session.WriteOnAsync(session.NodeLabel, request);
    }

    private void OnInbound(EndpointId source, Envelope env)
    {
        var stamped = EnvelopeIdentity.Stamp(source, env);
        switch (stamped.Kind)
        {
            case MessageKind.Response:
                HandleResponse(stamped);
                break;
            case MessageKind.Event:
                BroadcastEvent(source, stamped);
                break;
            case MessageKind.Request when Routes.IsHostCall(stamped.Route):
                if (!source.IsNode)
                {
                    _ = RejectWebHostCallAsync(source, stamped);
                }
                else
                {
                    _ = DispatchHostCallAsync(source, stamped);
                }
                break;
            case MessageKind.Request when Routes.IsPluginCall(stamped.Route):
                _ = RouteRequestAsync(stamped, source);
                break;
        }
    }

    private async Task RejectWebHostCallAsync(EndpointId source, Envelope env)
    {
        var reply = BuildHostCallReply(env, source, payload: null,
            BusError.For(ErrorCode.CapabilityDenied,
                "webview cannot call host.call.* directly; route through plugin.call.* to Node"));
        var key = SessionKey(source.PluginId, source.EntryId, source.SessionId);
        if (_sessions.TryGetValue(key, out var session))
        {
            await session.WriteOnAsync(source.EndpointLabel, reply);
        }
    }

    private async Task DispatchHostCallAsync(EndpointId source, Envelope env)
    {
        var key = SessionKey(source.PluginId, source.EntryId, source.SessionId);
        if (!_sessions.TryGetValue(key, out var session))
        {
            return;
        }

        if (!session.TryReserve(source.EndpointLabel, env.Id, env.Route))
        {
            await session.WriteOnAsync(source.EndpointLabel, BuildHostCallReply(env, source, payload: null,
                BusError.For(ErrorCode.TooManyRequests,
                    $"pending request limit {_pendingLimit} reached for endpoint {source.EndpointLabel}",
                    retryable: true)));
            return;
        }

        try
        {
            var capability = Routes.StripHostCall(env.Route);
            var decision = _gateway.Authorize(source.PluginId, source.EntryId, capability);
            _logger.LogInformation(
                "CapabilityAudit plugin={PluginId} entry={EntryId} route={Route} allowed={Allowed}",
                source.PluginId, source.EntryId, capability, decision.IsAllowed);

            Envelope reply;
            if (!decision.IsAllowed)
            {
                reply = BuildHostCallReply(env, source, payload: null, decision.Error);
            }
            else if (!_hostCallHandlers.TryGetValue(HandlerKey(source.PluginId, source.EntryId), out var invoker))
            {
                reply = BuildHostCallReply(env, source, payload: null,
                    BusError.For(ErrorCode.InternalError, $"no host call handler for {source.PluginId}/{source.EntryId}"));
            }
            else
            {
                try
                {
                    var method = Routes.StripHostCall(env.Route);
                    var parameters = env.Payload is null
                        ? JsonDocument.Parse("{}").RootElement.Clone()
                        : JsonDocument.Parse(env.Payload.ToJsonString()).RootElement.Clone();
                    var result = await invoker(method, parameters, CancellationToken.None);
                    reply = BuildHostCallReply(env, source,
                        payload: JsonNode.Parse(result.GetRawText()), error: null);
                }
                catch (Exception ex)
                {
                    reply = BuildHostCallReply(env, source, payload: null,
                        BusError.For(ErrorCode.InternalError, ex.Message));
                }
            }

            if (session.NodeLabel is not null)
            {
                await session.WriteOnAsync(session.NodeLabel, reply);
            }
        }
        finally
        {
            session.Release(source.EndpointLabel, env.Id, env.Route);
        }
    }

    private Envelope BuildHostCallReply(Envelope request, EndpointId source, JsonNode? payload, BusError? error)
        => new()
        {
            Version = ProtocolVersion.Current,
            Id = _ids.NewId(),
            CorrelationId = request.Id,
            TraceId = request.TraceId,
            SessionId = source.SessionId,
            PluginId = source.PluginId,
            EntryId = source.EntryId,
            EndpointId = EndpointIds.Host,
            Kind = MessageKind.Response,
            Route = request.Route,
            Payload = payload,
            Error = error,
        };

    private Envelope BuildErrorReply(Envelope request, EndpointId origin, BusError error) => new()
    {
        Version = ProtocolVersion.Current,
        Id = _ids.NewId(),
        CorrelationId = request.Id,
        TraceId = request.TraceId,
        SessionId = origin.SessionId,
        PluginId = origin.PluginId,
        EntryId = origin.EntryId,
        EndpointId = origin.EndpointLabel,
        Kind = MessageKind.Response,
        Route = request.Route,
        Error = error,
    };

    private void HandleResponse(Envelope env)
    {
        if (env.CorrelationId is null) return;
        if (!_pending.TryRemove(env.CorrelationId, out var pending)) return;

        var key = SessionKey(pending.Origin.PluginId, pending.Origin.EntryId, pending.Origin.SessionId);
        if (!_sessions.TryGetValue(key, out var session)) return;

        session.Release(pending.Origin.EndpointLabel, env.CorrelationId, pending.Route);
        var elapsedMs = Stopwatch.GetElapsedTime(pending.StartedAt).TotalMilliseconds;
        _logger.LogDebug(
            "Response correlated id={Corr} route={Route} result={Result} elapsedMs={ElapsedMs:0}",
            env.CorrelationId, env.Route, env.Error is null ? "ok" : env.Error.Code.ToString(), elapsedMs);
        _ = session.WriteOnAsync(pending.Origin.EndpointLabel, env);
    }

    private void BroadcastEvent(EndpointId source, Envelope env)
    {
        var key = SessionKey(source.PluginId, source.EntryId, source.SessionId);
        if (!_sessions.TryGetValue(key, out var session)) return;
        var droppedBefore = session.DroppedEvents;
        session.BroadcastExcept(source.EndpointLabel, env);
        var dropped = session.DroppedEvents - droppedBefore;
        if (dropped > 0)
        {
            _logger.LogWarning(
                "DroppedEvents plugin={PluginId} entry={EntryId} session={SessionId} route={Route} droppedDelta={Delta} total={Total}",
                source.PluginId, source.EntryId, source.SessionId, env.Route, dropped, session.DroppedEvents);
        }
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

    /// <summary>
    /// Fails every pending request whose origin belongs to the given session (e.g. on Node
    /// disconnect). Delivers an error response envelope to each origin endpoint.
    /// </summary>
    public void FailPendingForSession(string pluginId, string entryId, string sessionId, BusError error)
    {
        foreach (var (requestId, pending) in _pending.ToArray())
        {
            if (pending.Origin.PluginId != pluginId || pending.Origin.EntryId != entryId
                || pending.Origin.SessionId != sessionId)
            {
                continue;
            }

            if (!_pending.TryRemove(requestId, out _)) continue;

            var key = SessionKey(pending.Origin.PluginId, pending.Origin.EntryId, pending.Origin.SessionId);
            if (!_sessions.TryGetValue(key, out var session)) continue;

            session.Release(pending.Origin.EndpointLabel, requestId, pending.Route);
            var fail = new Envelope
            {
                Version = ProtocolVersion.Current,
                Id = _ids.NewId(),
                CorrelationId = requestId,
                TraceId = requestId,
                SessionId = sessionId,
                PluginId = pluginId,
                EntryId = entryId,
                EndpointId = EndpointIds.NodeMain,
                Kind = MessageKind.Response,
                Route = pending.Route,
                Error = error,
            };
            _ = session.WriteOnAsync(pending.Origin.EndpointLabel, fail);
        }
    }

    private static string SessionKey(string pluginId, string entryId, string sessionId)
        => $"{pluginId}\u001f{entryId}\u001f{sessionId}";

    private static string HandlerKey(string pluginId, string entryId)
        => $"{pluginId}\u001f{entryId}";

    private readonly record struct PendingOrigin(EndpointId Origin, string Route, long StartedAt);

    private sealed class SessionEndpoints
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, EndpointSlot> _byLabel = new();
        private readonly int _pendingLimit;
        private readonly int _eventCapacity;

        public SessionEndpoints(int pendingLimit, int eventCapacity)
        {
            _pendingLimit = pendingLimit;
            _eventCapacity = eventCapacity;
        }

        public string? NodeLabel { get; private set; }

        public long DroppedEvents
        {
            get
            {
                lock (_gate)
                {
                    long total = 0;
                    foreach (var slot in _byLabel.Values)
                    {
                        total += slot.EventQueue.DroppedEvents;
                    }
                    return total;
                }
            }
        }

        public void Add(EndpointId id, IMessageTransport transport, Action<Envelope> handler)
        {
            lock (_gate)
            {
                _byLabel[id.EndpointLabel] = new EndpointSlot(
                    id, transport, handler,
                    new PendingRequestTracker(_pendingLimit),
                    new BoundedEventQueue<Envelope>(_eventCapacity));
                if (id.IsNode) NodeLabel = id.EndpointLabel;
            }
        }

        public bool TryRemove(EndpointId id, out IMessageTransport transport, out Action<Envelope> handler)
        {
            lock (_gate)
            {
                if (_byLabel.Remove(id.EndpointLabel, out var ep))
                {
                    if (NodeLabel == id.EndpointLabel) NodeLabel = null;
                    transport = ep.Transport;
                    handler = ep.Handler;
                    return true;
                }
            }

            transport = null!;
            handler = null!;
            return false;
        }

        public bool TryReserve(string label, string requestId, string route)
        {
            lock (_gate)
            {
                return _byLabel.TryGetValue(label, out var slot)
                       && slot.Pending.TryReserve(requestId, route);
            }
        }

        public void Release(string label, string requestId, string route)
        {
            lock (_gate)
            {
                if (_byLabel.TryGetValue(label, out var slot))
                {
                    slot.Pending.Release(requestId, route);
                }
            }
        }

        public Task WriteOnAsync(string label, Envelope env)
        {
            IMessageTransport? t;
            lock (_gate) t = _byLabel.TryGetValue(label, out var ep) ? ep.Transport : null;
            return t is null ? Task.CompletedTask : t.SendAsync(env, CancellationToken.None).AsTask();
        }

        public void BroadcastExcept(string sourceLabel, Envelope env)
            => EnqueueAndDrain(env, excludeLabel: sourceLabel);

        public void BroadcastToAll(Envelope env)
            => EnqueueAndDrain(env, excludeLabel: null);

        private void EnqueueAndDrain(Envelope env, string? excludeLabel)
        {
            List<EndpointSlot> toKick;
            lock (_gate)
            {
                toKick = new List<EndpointSlot>(_byLabel.Count);
                foreach (var (label, slot) in _byLabel)
                {
                    if (excludeLabel is not null && label == excludeLabel) continue;
                    slot.EventQueue.Enqueue(env);
                    if (!slot.Draining)
                    {
                        slot.Draining = true;
                        toKick.Add(slot);
                    }
                }
            }

            foreach (var slot in toKick)
            {
                _ = DrainEventsAsync(slot);
            }
        }

        private async Task DrainEventsAsync(EndpointSlot slot)
        {
            try
            {
                while (true)
                {
                    IReadOnlyList<Envelope> batch;
                    lock (_gate)
                    {
                        batch = slot.EventQueue.Drain();
                        if (batch.Count == 0)
                        {
                            slot.Draining = false;
                            return;
                        }
                    }

                    foreach (var item in batch)
                    {
                        await slot.Transport.SendAsync(item, CancellationToken.None);
                    }
                }
            }
            catch
            {
                lock (_gate)
                {
                    slot.Draining = false;
                }
            }
        }

        private sealed class EndpointSlot
        {
            public EndpointSlot(
                EndpointId id,
                IMessageTransport transport,
                Action<Envelope> handler,
                PendingRequestTracker pending,
                BoundedEventQueue<Envelope> eventQueue)
            {
                Id = id;
                Transport = transport;
                Handler = handler;
                Pending = pending;
                EventQueue = eventQueue;
            }

            public EndpointId Id { get; }
            public IMessageTransport Transport { get; }
            public Action<Envelope> Handler { get; }
            public PendingRequestTracker Pending { get; }
            public BoundedEventQueue<Envelope> EventQueue { get; }
            public bool Draining;
        }
    }
}
