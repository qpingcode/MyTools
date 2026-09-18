using System.Collections.Concurrent;
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
/// Routes envelopes between transport-bound endpoints. RPC waiting, timeout, cancellation,
/// business execution, and event queueing belong to dedicated collaborators.
/// </summary>
public sealed class MessageBus : IMessageRouter
{
    public const int DefaultPendingLimit = 64;
    public const int DefaultEventQueueCapacity = EventFanout.DefaultQueueCapacity;

    private readonly ConcurrentDictionary<string, SessionEndpoints> _sessions = new();
    private readonly ConcurrentDictionary<string, ResponseRoute> _responseRoutes = new();
    private readonly HostCallDispatcher _hostCallDispatcher;
    private readonly EventFanout _eventFanout;
    private readonly IIdGenerator _ids;
    private readonly ILogger _logger;
    private readonly IPluginDiagnosticsService? _diagnostics;
    private readonly int _externalRequestLimit;

    public MessageBus(
        CapabilityGateway? gateway = null,
        IIdGenerator? ids = null,
        ILogger? logger = null,
        IPluginDiagnosticsService? diagnostics = null,
        int pendingLimit = DefaultPendingLimit,
        int eventQueueCapacity = DefaultEventQueueCapacity,
        HostCallDispatcher? hostCallDispatcher = null,
        EventFanout? eventFanout = null)
    {
        var effectiveGateway = gateway ?? new CapabilityGateway();
        _ids = ids ?? new GuidIdGenerator();
        _logger = logger ?? NullLogger.Instance;
        _diagnostics = diagnostics;
        _externalRequestLimit = pendingLimit;
        _hostCallDispatcher = hostCallDispatcher
            ?? new HostCallDispatcher(effectiveGateway, _ids, diagnostics, _logger, pendingLimit);
        _eventFanout = eventFanout
            ?? new EventFanout(diagnostics, _logger, eventQueueCapacity);
        _hostCallDispatcher.AttachRouter(this);
    }

    public HostCallDispatcher HostCallDispatcher => _hostCallDispatcher;
    public long TotalDroppedEvents => _eventFanout.TotalDroppedEvents;
    internal int ResponseRouteCount => _responseRoutes.Count;

    public void RegisterEndpoint(EndpointId endpoint, IMessageTransport transport)
    {
        var session = _sessions.GetOrAdd(
            SessionKey(endpoint.PluginId, endpoint.SessionId),
            _ => new SessionEndpoints());
        Action<Envelope> handler = envelope => OnInbound(endpoint, envelope);
        var binding = new EndpointBinding(
            endpoint,
            transport,
            handler,
            new PendingRequestTracker(_externalRequestLimit));

        if (session.AddOrReplace(binding, out var replaced))
        {
            replaced.Transport.MessageReceived -= replaced.Handler;
            _eventFanout.UnregisterEndpoint(replaced.Id);
            _hostCallDispatcher.RemoveEndpoint(replaced.Id);
            RemoveResponseRoutes(route => route.Origin == replaced.Id);
        }

        transport.MessageReceived += handler;
        _eventFanout.RegisterEndpoint(endpoint, transport);
        UpdateAdmissionDiagnostics(binding);
        _logger.LogDebug(
            "Bus endpoint registered plugin={PluginId} session={SessionId} ep={Endpoint} isNode={IsNode}",
            endpoint.PluginId,
            endpoint.SessionId,
            endpoint.EndpointLabel,
            endpoint.IsNode);
    }

    public void UnregisterEndpoint(EndpointId endpoint)
    {
        var key = SessionKey(endpoint.PluginId, endpoint.SessionId);
        if (!_sessions.TryGetValue(key, out var session)
            || !session.TryRemove(endpoint.EndpointLabel, out var binding))
        {
            return;
        }

        binding.Transport.MessageReceived -= binding.Handler;
        _eventFanout.UnregisterEndpoint(endpoint);
        _hostCallDispatcher.RemoveEndpoint(endpoint);
        RemoveResponseRoutes(route => route.Origin == endpoint);
        _diagnostics?.RemoveEndpoint(endpoint.PluginId, endpoint.SessionId, endpoint.EndpointLabel);
        if (session.IsEmpty)
        {
            ((ICollection<KeyValuePair<string, SessionEndpoints>>)_sessions)
                .Remove(new KeyValuePair<string, SessionEndpoints>(key, session));
        }

        _logger.LogDebug(
            "Bus endpoint unregistered plugin={PluginId} session={SessionId} ep={Endpoint}",
            endpoint.PluginId,
            endpoint.SessionId,
            endpoint.EndpointLabel);
    }

    public async Task RouteRequestToNodeAsync(
        Envelope request,
        EndpointId origin,
        CancellationToken cancellationToken = default)
    {
        ValidateOutboundRequest(request, origin);
        var session = GetSession(origin);
        var originBinding = session.Get(origin.EndpointLabel)
            ?? throw RouteError(ErrorCode.TransportDisconnected, $"origin endpoint {origin} is unavailable");
        var node = session.Node
            ?? throw RouteError(
                ErrorCode.TransportDisconnected,
                $"no node endpoint registered for session {origin.PluginId}/{origin.SessionId}");

        var admissionReserved = false;
        if (!IsHostOwnedEndpoint(origin))
        {
            lock (originBinding.Gate)
            {
                admissionReserved = originBinding.Admission.TryReserve(request.Id, request.Route);
                UpdateAdmissionDiagnostics(originBinding);
            }
            if (!admissionReserved)
            {
                var message =
                    $"pending request limit {_externalRequestLimit} reached for endpoint {origin.EndpointLabel}";
                _diagnostics?.RecordCallRejected(
                    origin.PluginId,
                    origin.SessionId,
                    origin.EndpointLabel,
                    request.Route,
                    request.Id,
                    message);
                throw RouteError(ErrorCode.TooManyRequests, message, retryable: true);
            }
        }

        var responseRoute = new ResponseRoute(origin, request.Route, admissionReserved);
        if (!_responseRoutes.TryAdd(request.Id, responseRoute))
        {
            ReleaseAdmission(responseRoute, request.Id);
            throw RouteError(ErrorCode.InvalidPayload, $"duplicate request id '{request.Id}'");
        }

        try
        {
            _logger.Log(
                Routes.IsPing(request.Route) ? LogLevel.Trace : LogLevel.Debug,
                "RouteRequest id={Id} traceId={TraceId} route={Route} origin={Origin} -> node",
                request.Id,
                request.TraceId,
                request.Route,
                origin.EndpointLabel);
            await node.Transport.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RemoveResponseRoute(request.Id, responseRoute);
            throw;
        }
        catch (Exception exception)
        {
            RemoveResponseRoute(request.Id, responseRoute);
            throw new MessageRouteException(BusError.For(
                ErrorCode.TransportDisconnected,
                exception.Message,
                retryable: true));
        }
    }

    /// <summary>Compatibility entry point for existing in-process callers.</summary>
    public async Task RouteRequestAsync(Envelope request, EndpointId origin)
    {
        try
        {
            await RouteRequestToNodeAsync(request, origin, CancellationToken.None);
        }
        catch (MessageRouteException exception) when (exception.Error.Code == ErrorCode.TooManyRequests)
        {
            await SendToEndpointAsync(origin, BuildErrorReply(request, origin, exception.Error));
        }
        catch (MessageRouteException exception)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }
    }

    public bool AbandonResponseRoute(string requestId, EndpointId origin)
    {
        if (!_responseRoutes.TryGetValue(requestId, out var route) || route.Origin != origin)
        {
            return false;
        }
        return RemoveResponseRoute(requestId, route);
    }

    public async Task SendToEndpointAsync(
        EndpointId target,
        Envelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(SessionKey(target.PluginId, target.SessionId), out var session)
            || session.Get(target.EndpointLabel) is not { } binding)
        {
            throw RouteError(ErrorCode.TransportDisconnected, $"target endpoint {target} is unavailable");
        }

        try
        {
            await binding.Transport.SendAsync(envelope, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MessageRouteException(BusError.For(
                ErrorCode.TransportDisconnected,
                exception.Message,
                retryable: true));
        }
    }

    public Task BroadcastAsync(
        EndpointId sessionEndpoint,
        Envelope envelope,
        string? excludeEndpointId,
        CancellationToken cancellationToken = default)
        => _eventFanout.BroadcastAsync(
            sessionEndpoint,
            envelope,
            excludeEndpointId,
            cancellationToken);

    public Task BroadcastHostEventAsync(EndpointId sessionEndpoint, Envelope envelope)
        => BroadcastAsync(sessionEndpoint, envelope, excludeEndpointId: null);

    /// <summary>Removes undeliverable routes; their owning RPC clients complete caller tasks.</summary>
    public void ClearResponseRoutesForSession(string pluginId, string sessionId)
        => RemoveResponseRoutes(route =>
            route.Origin.PluginId == pluginId && route.Origin.SessionId == sessionId);

    private void OnInbound(EndpointId source, Envelope envelope)
    {
        var stamped = EnvelopeIdentity.Stamp(source, envelope);
        var validation = EnvelopeValidator.Validate(stamped);
        var route = RouteRules.Classify(stamped.Route);
        if (!validation.IsValid || !route.IsLegal)
        {
            _logger.LogWarning(
                "Dropping invalid envelope endpoint={Endpoint} route={Route} error={Error}",
                source.EndpointLabel,
                stamped.Route,
                validation.Error?.Message ?? route.Error?.Message);
            if (stamped.Kind == MessageKind.Request)
            {
                _ = TrySendErrorReplyAsync(source, stamped, validation.Error ?? route.Error!);
            }
            return;
        }

        switch (stamped.Kind)
        {
            case MessageKind.Response:
                HandleResponse(source, stamped);
                break;
            case MessageKind.Event:
                _ = BroadcastAsync(source, stamped, source.EndpointLabel);
                break;
            case MessageKind.Request when Routes.IsHostCall(stamped.Route):
                _ = DispatchHostCallAsync(source, stamped);
                break;
            case MessageKind.Request when Routes.IsPluginCall(stamped.Route):
            case MessageKind.Request when Routes.IsPing(stamped.Route):
                _ = RouteInboundRequestAsync(stamped, source);
                break;
            default:
                _ = TrySendErrorReplyAsync(
                    source,
                    stamped,
                    BusError.For(ErrorCode.RouteNotFound, $"route '{stamped.Route}' is not valid for {stamped.Kind}"));
                break;
        }
    }

    private async Task RouteInboundRequestAsync(Envelope request, EndpointId origin)
    {
        try
        {
            await RouteRequestToNodeAsync(request, origin, CancellationToken.None);
        }
        catch (MessageRouteException exception)
        {
            await TrySendErrorReplyAsync(origin, request, exception.Error);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to route inbound request id={Id} route={Route} origin={Origin}",
                request.Id,
                request.Route,
                origin.EndpointLabel);
        }
    }

    private async Task DispatchHostCallAsync(EndpointId source, Envelope request)
    {
        try
        {
            await _hostCallDispatcher.DispatchAsync(source, request);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Host call dispatch failed id={Id} route={Route} source={Source}",
                request.Id,
                request.Route,
                source.EndpointLabel);
        }
    }

    private void HandleResponse(EndpointId source, Envelope response)
    {
        if (response.CorrelationId is null
            || !_responseRoutes.TryGetValue(response.CorrelationId, out var route))
        {
            _logger.LogDebug(
                "Dropping response with unknown correlation id={CorrelationId}",
                response.CorrelationId);
            return;
        }

        if (!source.IsNode
            || source.PluginId != route.Origin.PluginId
            || source.SessionId != route.Origin.SessionId
            || response.Route != route.Route)
        {
            _logger.LogWarning(
                "Dropping response from invalid source={Source} correlation={CorrelationId} route={Route}",
                source,
                response.CorrelationId,
                response.Route);
            return;
        }

        if (!RemoveResponseRoute(response.CorrelationId, route))
        {
            return;
        }
        _ = DeliverResponseAsync(route.Origin, response);
    }

    private async Task DeliverResponseAsync(EndpointId origin, Envelope response)
    {
        try
        {
            await SendToEndpointAsync(origin, response, CancellationToken.None);
        }
        catch (MessageRouteException exception)
        {
            _logger.LogDebug(
                exception,
                "Could not deliver response correlation={CorrelationId} origin={Origin}",
                response.CorrelationId,
                origin);
        }
    }

    private async Task TrySendErrorReplyAsync(EndpointId origin, Envelope request, BusError error)
    {
        try
        {
            await SendToEndpointAsync(origin, BuildErrorReply(request, origin, error));
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not deliver route failure id={Id} origin={Origin}",
                request.Id,
                origin.EndpointLabel);
        }
    }

    private SessionEndpoints GetSession(EndpointId endpoint)
        => _sessions.TryGetValue(SessionKey(endpoint.PluginId, endpoint.SessionId), out var session)
            ? session
            : throw RouteError(
                ErrorCode.TransportDisconnected,
                $"session {endpoint.PluginId}/{endpoint.SessionId} is unavailable");

    private static void ValidateOutboundRequest(Envelope request, EndpointId origin)
    {
        var validation = EnvelopeValidator.Validate(request);
        if (!validation.IsValid)
        {
            throw new MessageRouteException(validation.Error!);
        }
        if (request.Kind != MessageKind.Request)
        {
            throw RouteError(ErrorCode.InvalidPayload, "only request envelopes can be routed to Node");
        }
        var route = RouteRules.Classify(request.Route);
        if (!route.IsLegal || (!Routes.IsPluginCall(request.Route) && !Routes.IsPing(request.Route)))
        {
            throw new MessageRouteException(
                route.Error ?? BusError.For(ErrorCode.RouteNotFound, $"route '{request.Route}' cannot target Node"));
        }
        if (request.PluginId != origin.PluginId
            || request.SessionId != origin.SessionId
            || request.EndpointId != origin.EndpointLabel)
        {
            throw RouteError(ErrorCode.InvalidPayload, "request identity does not match its origin endpoint");
        }
    }

    private bool RemoveResponseRoute(string requestId, ResponseRoute expected)
    {
        var removed = ((ICollection<KeyValuePair<string, ResponseRoute>>)_responseRoutes)
            .Remove(new KeyValuePair<string, ResponseRoute>(requestId, expected));
        if (!removed)
        {
            return false;
        }
        ReleaseAdmission(expected, requestId);
        return true;
    }

    private void RemoveResponseRoutes(Func<ResponseRoute, bool> predicate)
    {
        foreach (var pair in _responseRoutes.ToArray())
        {
            if (predicate(pair.Value))
            {
                RemoveResponseRoute(pair.Key, pair.Value);
            }
        }
    }

    private void ReleaseAdmission(ResponseRoute route, string requestId)
    {
        if (!route.AdmissionReserved
            || !_sessions.TryGetValue(
                SessionKey(route.Origin.PluginId, route.Origin.SessionId),
                out var session)
            || session.Get(route.Origin.EndpointLabel) is not { } binding)
        {
            return;
        }

        lock (binding.Gate)
        {
            binding.Admission.Release(requestId, route.Route);
            UpdateAdmissionDiagnostics(binding);
        }
    }

    private void UpdateAdmissionDiagnostics(EndpointBinding binding)
        => _diagnostics?.UpdateEndpointPending(
            binding.Id.PluginId,
            binding.Id.SessionId,
            binding.Id.EndpointLabel,
            binding.Admission.InFlight,
            binding.Admission.Limit,
            binding.Admission.HighWaterMark);

    private Envelope BuildErrorReply(Envelope request, EndpointId origin, BusError error)
        => new()
        {
            Version = ProtocolVersion.Current,
            Id = _ids.NewId(),
            CorrelationId = request.Id,
            TraceId = request.TraceId,
            SessionId = origin.SessionId,
            PluginId = origin.PluginId,
            EndpointId = EndpointIds.Host,
            Kind = MessageKind.Response,
            Route = request.Route,
            Error = error,
        };

    private static bool IsHostOwnedEndpoint(EndpointId endpoint)
        => endpoint.EndpointLabel is EndpointIds.Host or EndpointIds.HostControl;

    private static MessageRouteException RouteError(
        ErrorCode code,
        string message,
        bool retryable = false)
        => new(BusError.For(code, message, retryable));

    private static string SessionKey(string pluginId, string sessionId)
        => $"{pluginId}\u001f{sessionId}";

    private sealed record ResponseRoute(EndpointId Origin, string Route, bool AdmissionReserved);

    private sealed record EndpointBinding(
        EndpointId Id,
        IMessageTransport Transport,
        Action<Envelope> Handler,
        PendingRequestTracker Admission)
    {
        public object Gate { get; } = new();
    }

    private sealed class SessionEndpoints
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, EndpointBinding> _bindings = new();

        public EndpointBinding? Node
        {
            get
            {
                lock (_gate)
                {
                    return _bindings.Values.FirstOrDefault(binding => binding.Id.IsNode);
                }
            }
        }

        public bool IsEmpty
        {
            get
            {
                lock (_gate) return _bindings.Count == 0;
            }
        }

        public bool AddOrReplace(EndpointBinding binding, out EndpointBinding replaced)
        {
            lock (_gate)
            {
                var hadExisting = _bindings.Remove(binding.Id.EndpointLabel, out replaced!);
                _bindings.Add(binding.Id.EndpointLabel, binding);
                return hadExisting;
            }
        }

        public EndpointBinding? Get(string endpointLabel)
        {
            lock (_gate)
            {
                return _bindings.GetValueOrDefault(endpointLabel);
            }
        }

        public bool TryRemove(string endpointLabel, out EndpointBinding binding)
        {
            lock (_gate)
            {
                return _bindings.Remove(endpointLabel, out binding!);
            }
        }
    }
}
