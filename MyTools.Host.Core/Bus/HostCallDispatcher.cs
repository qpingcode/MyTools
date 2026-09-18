using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Host.Core.Backpressure;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Diagnostics;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Host.Core.Bus;

public delegate Task<JsonElement> HostCallInvoker(
    string method,
    JsonElement parameters,
    CancellationToken cancellationToken);

/// <summary>
/// Authorizes and executes Node-to-Host calls. Caller-declared timeout values are deliberately
/// ignored; handlers receive only their registered application/session lifecycle token.
/// </summary>
public sealed class HostCallDispatcher
{
    public const int DefaultConcurrentCallLimit = 64;

    private const string EmptyObjectJson = "{}";
    private readonly ConcurrentDictionary<string, HandlerRegistration> _handlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<EndpointId, AdmissionState> _admission = new();
    private readonly CapabilityGateway _gateway;
    private readonly IIdGenerator _ids;
    private readonly IPluginDiagnosticsService? _diagnostics;
    private readonly ILogger _logger;
    private readonly int _concurrentCallLimit;
    private IMessageRouter? _router;

    public HostCallDispatcher(
        CapabilityGateway gateway,
        IIdGenerator? ids = null,
        IPluginDiagnosticsService? diagnostics = null,
        ILogger? logger = null,
        int concurrentCallLimit = DefaultConcurrentCallLimit)
    {
        _gateway = gateway;
        _ids = ids ?? new GuidIdGenerator();
        _diagnostics = diagnostics;
        _logger = logger ?? NullLogger.Instance;
        _concurrentCallLimit = concurrentCallLimit;
    }

    internal void AttachRouter(IMessageRouter router)
    {
        if (_router is not null && !ReferenceEquals(_router, router))
        {
            throw new InvalidOperationException("host call dispatcher is already attached to a router");
        }
        _router = router;
    }

    public void RegisterHandler(
        string pluginId,
        HostCallInvoker handler,
        CancellationToken lifecycleToken = default)
        => _handlers[pluginId] = new HandlerRegistration(handler, lifecycleToken);

    public void UnregisterHandler(string pluginId)
        => _handlers.TryRemove(pluginId, out _);

    public async Task DispatchAsync(EndpointId source, Envelope request)
    {
        var router = _router ?? throw new InvalidOperationException("host call dispatcher has no router");
        if (!source.IsNode)
        {
            await SendReplyAsync(router, source, request, null,
                BusError.For(
                    ErrorCode.CapabilityDenied,
                    "webview cannot call host.call.* directly; route through plugin.call.* to Node"));
            return;
        }

        var admission = _admission.GetOrAdd(
            source,
            _ => new AdmissionState(new PendingRequestTracker(_concurrentCallLimit)));
        var admitted = false;
        lock (admission.Gate)
        {
            admitted = admission.Tracker.TryReserve(request.Id, request.Route);
            UpdatePendingDiagnostics(source, admission.Tracker);
        }
        if (!admitted)
        {
            var message =
                $"host call concurrency limit {_concurrentCallLimit} reached for endpoint {source.EndpointLabel}";
            _diagnostics?.RecordCallRejected(
                source.PluginId,
                source.SessionId,
                source.EndpointLabel,
                request.Route,
                request.Id,
                message);
            await SendReplyAsync(
                router,
                source,
                request,
                null,
                BusError.For(ErrorCode.TooManyRequests, message, retryable: true));
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        Envelope reply;
        try
        {
            var capability = Routes.StripHostCall(request.Route);
            var decision = _gateway.Authorize(source.PluginId, capability);
            _logger.LogDebug(
                "CapabilityAudit plugin={PluginId} route={Route} allowed={Allowed}",
                source.PluginId,
                capability,
                decision.IsAllowed);

            if (!decision.IsAllowed)
            {
                reply = BuildReply(request, source, null, decision.Error);
            }
            else if (!_handlers.TryGetValue(source.PluginId, out var registration))
            {
                reply = BuildReply(request, source, null,
                    BusError.For(ErrorCode.InternalError, $"no host call handler for {source.PluginId}"));
            }
            else
            {
                try
                {
                    var parameters = request.Payload is null
                        ? JsonDocument.Parse(EmptyObjectJson).RootElement.Clone()
                        : JsonDocument.Parse(request.Payload.ToJsonString()).RootElement.Clone();
                    var result = await registration.Handler(
                        capability,
                        parameters,
                        registration.LifecycleToken);
                    reply = BuildReply(
                        request,
                        source,
                        JsonNode.Parse(result.GetRawText()),
                        error: null);
                }
                catch (OperationCanceledException) when (registration.LifecycleToken.IsCancellationRequested)
                {
                    reply = BuildReply(request, source, null,
                        BusError.For(ErrorCode.TransportDisconnected, "host call lifecycle ended", retryable: true));
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Host call failed plugin={PluginId} route={Route} requestId={RequestId}",
                        source.PluginId,
                        request.Route,
                        request.Id);
                    reply = BuildReply(request, source, null,
                        BusError.For(ErrorCode.InternalError, exception.Message));
                }
            }

            await router.SendToEndpointAsync(source, reply, CancellationToken.None);
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            _diagnostics?.RecordCallCompleted(
                source.PluginId,
                source.SessionId,
                source.EndpointLabel,
                request.Route,
                request.Id,
                elapsed,
                reply.Error is null ? PluginCallOutcome.Success : PluginCallOutcome.Failure,
                reply.Error?.Message);
        }
        catch (MessageRouteException exception)
        {
            _logger.LogDebug(
                exception,
                "Dropping host call reply for unavailable endpoint plugin={PluginId} session={SessionId}",
                source.PluginId,
                source.SessionId);
        }
        finally
        {
            lock (admission.Gate)
            {
                admission.Tracker.Release(request.Id, request.Route);
                if (_admission.TryGetValue(source, out var current)
                    && ReferenceEquals(current, admission))
                {
                    UpdatePendingDiagnostics(source, admission.Tracker);
                }
            }
        }
    }

    public void RemoveEndpoint(EndpointId endpoint)
        => _admission.TryRemove(endpoint, out _);

    private Task SendReplyAsync(
        IMessageRouter router,
        EndpointId source,
        Envelope request,
        JsonNode? payload,
        BusError? error)
        => router.SendToEndpointAsync(
            source,
            BuildReply(request, source, payload, error),
            CancellationToken.None);

    private Envelope BuildReply(
        Envelope request,
        EndpointId source,
        JsonNode? payload,
        BusError? error)
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

    private void UpdatePendingDiagnostics(EndpointId endpoint, PendingRequestTracker tracker)
        => _diagnostics?.UpdateEndpointPending(
            endpoint.PluginId,
            endpoint.SessionId,
            endpoint.EndpointLabel,
            tracker.InFlight,
            tracker.Limit,
            tracker.HighWaterMark);

    private sealed record HandlerRegistration(
        HostCallInvoker Handler,
        CancellationToken LifecycleToken);

    private sealed record AdmissionState(PendingRequestTracker Tracker)
    {
        public object Gate { get; } = new();
    }
}
