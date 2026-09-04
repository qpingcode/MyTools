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
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Host.Core.Bus;

/// <summary>
/// Host-call handler registered per plugin. Invoked after <see cref="CapabilityGateway"/>
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
/// never trusts peer-declared plugin/session/endpoint ids. Per-endpoint
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
    private readonly IPluginDiagnosticsService? _diagnostics;
    private readonly int _pendingLimit;
    private readonly int _eventQueueCapacity;

    public MessageBus(
        CapabilityGateway? gateway = null,
        IIdGenerator? ids = null,
        ILogger? logger = null,
        IPluginDiagnosticsService? diagnostics = null,
        int pendingLimit = DefaultPendingLimit,
        int eventQueueCapacity = DefaultEventQueueCapacity)
    {
        _gateway = gateway ?? new CapabilityGateway();
        _ids = ids ?? new GuidIdGenerator();
        _logger = logger ?? NullLogger.Instance;
        _diagnostics = diagnostics;
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
        var key = SessionKey(id.PluginId, id.SessionId);
        var session = _sessions.GetOrAdd(key, _ => new SessionEndpoints(this, _pendingLimit, _eventQueueCapacity));
        Action<Envelope> handler = env => OnInbound(id, env);
        session.Add(id, transport, handler);
        transport.MessageReceived += handler;
        _logger.LogDebug(
            "Bus endpoint registered plugin={PluginId} session={SessionId} ep={Endpoint} isNode={IsNode}",
            id.PluginId, id.SessionId, id.EndpointLabel, id.IsNode);
    }

    public void UnregisterEndpoint(EndpointId id)
    {
        var key = SessionKey(id.PluginId, id.SessionId);
        if (_sessions.TryGetValue(key, out var session)
            && session.TryRemove(id, out var transport, out var handler))
        {
            transport.MessageReceived -= handler;
            _diagnostics?.RemoveEndpoint(id.PluginId, id.SessionId, id.EndpointLabel);
            _logger.LogDebug(
                "Bus endpoint unregistered plugin={PluginId} session={SessionId} ep={Endpoint}",
                id.PluginId, id.SessionId, id.EndpointLabel);
        }
    }

    public void RegisterHostCallHandler(string pluginId, HostCallInvoker handler)
        => _hostCallHandlers[pluginId] = handler;

    public void UnregisterHostCallHandler(string pluginId)
        => _hostCallHandlers.TryRemove(pluginId, out _);

    public void AbandonPendingRequest(string requestId, string route)
    {
        if (!_pending.TryRemove(requestId, out var pending))
        {
            return;
        }

        var key = SessionKey(pending.Origin.PluginId, pending.Origin.SessionId);
        if (_sessions.TryGetValue(key, out var session))
        {
            session.Release(pending.Origin.EndpointLabel, requestId, route);
        }
    }

    /// <summary>
    /// Routes a request from <paramref name="origin"/> to the Node endpoint of the same session.
    /// Records the correlation so the eventual response returns to the origin. Rejects with
    /// <see cref="ErrorCode.TooManyRequests"/> when the origin's pending cap is reached.
    /// </summary>
    public async Task RouteRequestAsync(Envelope request, EndpointId origin)
    {
        var key = SessionKey(origin.PluginId, origin.SessionId);
        if (!_sessions.TryGetValue(key, out var session) || session.NodeLabel is null)
        {
            throw new InvalidOperationException($"no node endpoint registered for session {key}");
        }

        if (!session.TryReserve(origin.EndpointLabel, request.Id, request.Route))
        {
            if (!Routes.IsPing(request.Route))
            {
                _diagnostics?.RecordCallRejected(
                    origin.PluginId,
                    origin.SessionId,
                    origin.EndpointLabel,
                    request.Route,
                    request.Id,
                    $"pending request limit {_pendingLimit} reached for endpoint {origin.EndpointLabel}");
            }
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
        _logger.Log(
            Routes.IsPing(request.Route) ? LogLevel.Trace : LogLevel.Debug,
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
                _ = RouteInboundRequestAsync(stamped, source);
                break;
        }
    }

    private async Task RouteInboundRequestAsync(Envelope request, EndpointId origin)
    {
        try
        {
            await RouteRequestAsync(request, origin);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Cannot route inbound request id={Id} route={Route} origin={Origin}",
                request.Id, request.Route, origin.EndpointLabel);
            try
            {
                var key = SessionKey(origin.PluginId, origin.SessionId);
                if (_sessions.TryGetValue(key, out var session))
                {
                    await session.WriteOnAsync(origin.EndpointLabel, BuildErrorReply(request, origin,
                        BusError.For(ErrorCode.TransportDisconnected, ex.Message, retryable: true)));
                }
            }
            catch (Exception replyException)
            {
                _logger.LogDebug(replyException,
                    "Could not deliver route failure id={Id} to origin={Origin}",
                    request.Id, origin.EndpointLabel);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to route inbound request id={Id} route={Route} origin={Origin}",
                request.Id, request.Route, origin.EndpointLabel);
        }
    }

    private async Task RejectWebHostCallAsync(EndpointId source, Envelope env)
    {
        var reply = BuildHostCallReply(env, source, payload: null,
            BusError.For(ErrorCode.CapabilityDenied,
                "webview cannot call host.call.* directly; route through plugin.call.* to Node"));
        var key = SessionKey(source.PluginId, source.SessionId);
        if (_sessions.TryGetValue(key, out var session))
        {
            await session.WriteOnAsync(source.EndpointLabel, reply);
        }
    }

    private async Task DispatchHostCallAsync(EndpointId source, Envelope env)
    {
        var key = SessionKey(source.PluginId, source.SessionId);
        if (!_sessions.TryGetValue(key, out var session))
        {
            return;
        }

        if (!session.TryReserve(source.EndpointLabel, env.Id, env.Route))
        {
            if (!Routes.IsPing(env.Route))
            {
                _diagnostics?.RecordCallRejected(
                    source.PluginId,
                    source.SessionId,
                    source.EndpointLabel,
                    env.Route,
                    env.Id,
                    $"pending request limit {_pendingLimit} reached for endpoint {source.EndpointLabel}");
            }
            await session.WriteOnAsync(source.EndpointLabel, BuildHostCallReply(env, source, payload: null,
                BusError.For(ErrorCode.TooManyRequests,
                    $"pending request limit {_pendingLimit} reached for endpoint {source.EndpointLabel}",
                    retryable: true)));
            return;
        }

        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            var capability = Routes.StripHostCall(env.Route);
            var decision = _gateway.Authorize(source.PluginId, capability);
            _logger.LogDebug(
                "CapabilityAudit plugin={PluginId} route={Route} allowed={Allowed}",
                source.PluginId, capability, decision.IsAllowed);

            Envelope reply;
            if (!decision.IsAllowed)
            {
                reply = BuildHostCallReply(env, source, payload: null, decision.Error);
            }
            else if (!_hostCallHandlers.TryGetValue(source.PluginId, out var invoker))
            {
                reply = BuildHostCallReply(env, source, payload: null,
                    BusError.For(ErrorCode.InternalError, $"no host call handler for {source.PluginId}"));
            }
            else
            {
                try
                {
                    var method = Routes.StripHostCall(env.Route);
                    var parameters = env.Payload is null
                        ? JsonDocument.Parse("{}").RootElement.Clone()
                        : JsonDocument.Parse(env.Payload.ToJsonString()).RootElement.Clone();
                    using var timeoutCts = env.TimeoutMs is > 0
                        ? new CancellationTokenSource(env.TimeoutMs.Value)
                        : null;
                    var result = await invoker(method, parameters, timeoutCts?.Token ?? CancellationToken.None);
                    reply = BuildHostCallReply(env, source,
                        payload: JsonNode.Parse(result.GetRawText()), error: null);
                }
                catch (OperationCanceledException) when (env.TimeoutMs is > 0)
                {
                    reply = BuildHostCallReply(env, source, payload: null,
                        BusError.For(ErrorCode.RequestTimeout,
                            $"host.call '{env.Route}' timed out after {env.TimeoutMs}ms",
                            retryable: true));
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Host call failed plugin={PluginId} route={Route} requestId={RequestId}",
                        source.PluginId,
                        env.Route,
                        env.Id);
                    reply = BuildHostCallReply(env, source, payload: null,
                        BusError.For(ErrorCode.InternalError, ex.Message));
                }
            }

            if (session.NodeLabel is not null)
            {
                await session.WriteOnAsync(session.NodeLabel, reply);
            }

            if (!Routes.IsPing(env.Route))
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                if (reply.Error?.Code == ErrorCode.RequestTimeout)
                {
                    _diagnostics?.RecordCallTimeout(
                        source.PluginId,
                        source.SessionId,
                        source.EndpointLabel,
                        env.Route,
                        env.Id,
                        elapsedMs,
                        reply.Error.Message);
                }
                else
                {
                    _diagnostics?.RecordCallCompleted(
                        source.PluginId,
                        source.SessionId,
                        source.EndpointLabel,
                        env.Route,
                        env.Id,
                        elapsedMs,
                        reply.Error is null ? PluginCallOutcome.Success : PluginCallOutcome.Failure,
                        reply.Error?.Message);
                }
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
        EndpointId = origin.EndpointLabel,
        Kind = MessageKind.Response,
        Route = request.Route,
        Error = error,
    };

    private void HandleResponse(Envelope env)
    {
        if (env.CorrelationId is null) return;
        if (!_pending.TryRemove(env.CorrelationId, out var pending)) return;

        var key = SessionKey(pending.Origin.PluginId, pending.Origin.SessionId);
        if (!_sessions.TryGetValue(key, out var session)) return;

        session.Release(pending.Origin.EndpointLabel, env.CorrelationId, pending.Route);
        var elapsedMs = Stopwatch.GetElapsedTime(pending.StartedAt).TotalMilliseconds;
        if (!Routes.IsPing(pending.Route))
        {
            _diagnostics?.RecordCallCompleted(
                pending.Origin.PluginId,
                pending.Origin.SessionId,
                pending.Origin.EndpointLabel,
                env.Route,
                env.CorrelationId,
                elapsedMs,
                env.Error is null ? PluginCallOutcome.Success : PluginCallOutcome.Failure,
                env.Error?.Message);
        }
        _logger.Log(
            Routes.IsPing(pending.Route) ? LogLevel.Trace : LogLevel.Debug,
            "Response correlated id={Corr} route={Route} result={Result} elapsedMs={ElapsedMs:0}",
            env.CorrelationId, env.Route, env.Error is null ? "ok" : env.Error.Code.ToString(), elapsedMs);
        _ = session.WriteOnAsync(pending.Origin.EndpointLabel, env);
    }

    private void BroadcastEvent(EndpointId source, Envelope env)
    {
        var key = SessionKey(source.PluginId, source.SessionId);
        if (!_sessions.TryGetValue(key, out var session)) return;
        session.BroadcastExcept(source.EndpointLabel, env);
    }

    /// <summary>
    /// Broadcasts a <c>host.event.*</c> envelope to every endpoint in the target session. The host
    /// is not itself an endpoint, so there is no source to exclude.
    /// </summary>
    public Task BroadcastHostEventAsync(EndpointId anyEndpointInSession, Envelope env)
    {
        var key = SessionKey(anyEndpointInSession.PluginId, anyEndpointInSession.SessionId);
        if (!_sessions.TryGetValue(key, out var session)) return Task.CompletedTask;
        session.BroadcastToAll(env);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fails every pending request whose origin belongs to the given session (e.g. on Node
    /// disconnect). Delivers an error response envelope to each origin endpoint.
    /// </summary>
    public void FailPendingForSession(string pluginId, string sessionId, BusError error)
    {
        foreach (var (requestId, pending) in _pending.ToArray())
        {
            if (pending.Origin.PluginId != pluginId || pending.Origin.SessionId != sessionId)
            {
                continue;
            }

            if (!_pending.TryRemove(requestId, out _)) continue;

            var key = SessionKey(pending.Origin.PluginId, pending.Origin.SessionId);
            if (!_sessions.TryGetValue(key, out var session)) continue;

            session.Release(pending.Origin.EndpointLabel, requestId, pending.Route);
            var elapsedMs = Stopwatch.GetElapsedTime(pending.StartedAt).TotalMilliseconds;
            if (!Routes.IsPing(pending.Route))
            {
                _diagnostics?.RecordCallCompleted(
                    pending.Origin.PluginId,
                    pending.Origin.SessionId,
                    pending.Origin.EndpointLabel,
                    pending.Route,
                    requestId,
                    elapsedMs,
                    PluginCallOutcome.Failure,
                    error.Message);
            }
            var fail = new Envelope
            {
                Version = ProtocolVersion.Current,
                Id = _ids.NewId(),
                CorrelationId = requestId,
                TraceId = requestId,
                SessionId = sessionId,
                PluginId = pluginId,
                EndpointId = EndpointIds.NodeMain,
                Kind = MessageKind.Response,
                Route = pending.Route,
                Error = error,
            };
            _ = session.WriteOnAsync(pending.Origin.EndpointLabel, fail);
        }
    }

    private static string SessionKey(string pluginId, string sessionId)
        => $"{pluginId}\u001f{sessionId}";

    private readonly record struct PendingOrigin(EndpointId Origin, string Route, long StartedAt);
    private readonly record struct QueuedEvent(Envelope Envelope, long EnqueuedAt);

    private sealed class SessionEndpoints
    {
        private readonly MessageBus _owner;
        private readonly object _gate = new();
        private readonly Dictionary<string, EndpointSlot> _byLabel = new();
        private readonly int _pendingLimit;
        private readonly int _eventCapacity;

        public SessionEndpoints(MessageBus owner, int pendingLimit, int eventCapacity)
        {
            _owner = owner;
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
                    new BoundedEventQueue<QueuedEvent>(_eventCapacity));
                if (id.IsNode) NodeLabel = id.EndpointLabel;
                _owner.UpdatePendingDiagnostics(id, _byLabel[id.EndpointLabel].Pending);
                _owner.UpdateEventQueueDiagnostics(id, _byLabel[id.EndpointLabel].EventQueue);
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
                var reserved = _byLabel.TryGetValue(label, out var slot)
                               && slot.Pending.TryReserve(requestId, route);
                if (slot is not null)
                {
                    _owner.UpdatePendingDiagnostics(slot.Id, slot.Pending);
                }

                return reserved;
            }
        }

        public void Release(string label, string requestId, string route)
        {
            lock (_gate)
            {
                if (_byLabel.TryGetValue(label, out var slot))
                {
                    slot.Pending.Release(requestId, route);
                    _owner.UpdatePendingDiagnostics(slot.Id, slot.Pending);
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
                    var queued = new QueuedEvent(env, Stopwatch.GetTimestamp());
                    var dropped = slot.EventQueue.TryEnqueue(queued, out var droppedItem);
                    _owner.RecordEventQueuedDiagnostics(
                        slot.Id,
                        env.Route,
                        slot.EventQueue,
                        dropped,
                        dropped ? droppedItem.Envelope.Route : null);
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
                    IReadOnlyList<QueuedEvent> batch;
                    lock (_gate)
                    {
                        batch = slot.EventQueue.Drain();
                        _owner.UpdateEventQueueDiagnostics(slot.Id, slot.EventQueue);
                        if (batch.Count == 0)
                        {
                            slot.Draining = false;
                            return;
                        }
                    }

                    foreach (var item in batch)
                    {
                        var queueWaitMs = Stopwatch.GetElapsedTime(item.EnqueuedAt).TotalMilliseconds;
                        await slot.Transport.SendAsync(item.Envelope, CancellationToken.None);
                        var deliveryMs = Stopwatch.GetElapsedTime(item.EnqueuedAt).TotalMilliseconds;
                        int depth;
                        int capacity;
                        int highWaterMark;
                        long droppedTotal;
                        double oldestWaitMs;
                        lock (_gate)
                        {
                            depth = slot.EventQueue.Count;
                            capacity = slot.EventQueue.Capacity;
                            highWaterMark = slot.EventQueue.HighWaterMark;
                            droppedTotal = slot.EventQueue.DroppedEvents;
                            oldestWaitMs = GetOldestWaitMs(slot.EventQueue);
                        }
                        _owner.RecordEventDeliveredDiagnostics(
                            slot.Id,
                            item.Envelope.Route,
                            queueWaitMs,
                            deliveryMs,
                            depth,
                            capacity,
                            highWaterMark,
                            droppedTotal,
                            oldestWaitMs);
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
                BoundedEventQueue<QueuedEvent> eventQueue)
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
            public BoundedEventQueue<QueuedEvent> EventQueue { get; }
            public bool Draining;
        }
    }

    private void UpdatePendingDiagnostics(EndpointId id, PendingRequestTracker pending)
    {
        _diagnostics?.UpdateEndpointPending(
            id.PluginId,
            id.SessionId,
            id.EndpointLabel,
            pending.InFlight,
            pending.Limit,
            pending.HighWaterMark);
    }

    private void UpdateEventQueueDiagnostics(EndpointId id, BoundedEventQueue<QueuedEvent> queue)
    {
        _diagnostics?.UpdateEventQueueState(
            id.PluginId,
            id.SessionId,
            id.EndpointLabel,
            queue.Count,
            queue.Capacity,
            queue.HighWaterMark,
            queue.DroppedEvents,
            GetOldestWaitMs(queue));
    }

    private void RecordEventQueuedDiagnostics(
        EndpointId id,
        string route,
        BoundedEventQueue<QueuedEvent> queue,
        bool dropped,
        string? droppedRoute)
    {
        _diagnostics?.RecordEventQueued(
            id.PluginId,
            id.SessionId,
            id.EndpointLabel,
            route,
            queue.Count,
            queue.Capacity,
            queue.HighWaterMark,
            queue.DroppedEvents,
            dropped,
            GetOldestWaitMs(queue),
            droppedRoute);
    }

    private void RecordEventDeliveredDiagnostics(
        EndpointId id,
        string route,
        double queueWaitMs,
        double deliveryMs,
        int depth,
        int capacity,
        int highWaterMark,
        long droppedTotal,
        double oldestWaitMs)
    {
        _diagnostics?.RecordEventDelivered(
            id.PluginId,
            id.SessionId,
            id.EndpointLabel,
            route,
            queueWaitMs,
            deliveryMs,
            depth,
            capacity,
            highWaterMark,
            droppedTotal,
            oldestWaitMs);
    }

    private static double GetOldestWaitMs(BoundedEventQueue<QueuedEvent> queue)
    {
        return queue.TryPeek(out var queued)
            ? Stopwatch.GetElapsedTime(queued.EnqueuedAt).TotalMilliseconds
            : 0;
    }
}
