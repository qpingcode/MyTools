using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Host.Core.Bus;

public interface IEndpointRpcClient : IAsyncDisposable
{
    EndpointId Endpoint { get; }

    Task<JsonNode?> CallAsync(
        string route,
        JsonNode? payload,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    void FailPending(BusError error);
}

public class RpcCallException : Exception
{
    public RpcCallException(ErrorCode code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
        DetailMessage = message;
    }

    public ErrorCode Code { get; }
    public string DetailMessage { get; }
}

/// <summary>Owns one Host endpoint's pending RPC calls, timeouts, cancellation, and backpressure.</summary>
public sealed class EndpointRpcClient : IEndpointRpcClient
{
    public const int DefaultPendingLimit = 64;

    private readonly IMessageRouter _router;
    private readonly HostEndpointTransport _transport;
    private readonly IIdGenerator _ids;
    private readonly IPluginDiagnosticsService? _diagnostics;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, PendingCall> _pending = new();
    private readonly SemaphoreSlim _slots;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private int _disposed;
    private int _pendingHighWaterMark;

    public EndpointRpcClient(
        IMessageRouter router,
        EndpointId endpoint,
        IIdGenerator? ids = null,
        IPluginDiagnosticsService? diagnostics = null,
        ILogger? logger = null,
        int pendingLimit = DefaultPendingLimit)
    {
        if (endpoint.IsNode)
        {
            throw new ArgumentException("an RPC client endpoint must be host-owned", nameof(endpoint));
        }
        if (pendingLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingLimit));
        }

        _router = router;
        Endpoint = endpoint;
        _ids = ids ?? new GuidIdGenerator();
        _diagnostics = diagnostics;
        _logger = logger ?? NullLogger.Instance;
        PendingLimit = pendingLimit;
        _slots = new SemaphoreSlim(pendingLimit, pendingLimit);
        _transport = new HostEndpointTransport();
        _transport.Delivered += OnDelivery;
        _router.RegisterEndpoint(endpoint, _transport);
        UpdatePendingDiagnostics();
    }

    public EndpointId Endpoint { get; }
    public int PendingLimit { get; }
    internal int PendingCount => _pending.Count;
    public event Action<Envelope>? EventReceived;

    public async Task<JsonNode?> CallAsync(
        string route,
        JsonNode? payload,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (!await _slots.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            var message = $"pending request limit {PendingLimit} reached for endpoint {Endpoint.EndpointLabel}";
            _diagnostics?.RecordCallRejected(
                Endpoint.PluginId,
                Endpoint.SessionId,
                Endpoint.EndpointLabel,
                route,
                string.Empty,
                message);
            throw new RpcCallException(ErrorCode.TooManyRequests, message);
        }

        var requestId = _ids.NewId();
        var startedAt = Stopwatch.GetTimestamp();
        var completion = new TaskCompletionSource<Envelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingCall(route, completion, startedAt);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
            if (!_pending.TryAdd(requestId, pending))
            {
                throw new RpcCallException(ErrorCode.InternalError, $"duplicate local request id '{requestId}'");
            }
            UpdatePendingHighWaterMark();
            UpdatePendingDiagnostics();

            var timeoutMilliseconds = checked((int)Math.Ceiling(timeout.TotalMilliseconds));
            var request = new Envelope
            {
                Version = ProtocolVersion.Current,
                Id = requestId,
                TraceId = requestId,
                SessionId = Endpoint.SessionId,
                PluginId = Endpoint.PluginId,
                EndpointId = Endpoint.EndpointLabel,
                Kind = MessageKind.Request,
                Route = route,
                TimeoutMs = timeoutMilliseconds,
                Payload = payload,
            };

            try
            {
                await _router.RouteRequestToNodeAsync(request, Endpoint, cancellationToken);
            }
            catch (MessageRouteException exception)
            {
                CompleteWithError(requestId, pending, exception.Error);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CompleteWithError(
                    requestId,
                    pending,
                    BusError.For(ErrorCode.Cancelled, $"call '{route}' was cancelled"));
            }
            catch (Exception exception)
            {
                CompleteWithError(
                    requestId,
                    pending,
                    BusError.For(ErrorCode.TransportDisconnected, exception.Message, retryable: true));
            }

            Envelope response;
            try
            {
                response = await completion.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                if (TryAbandon(requestId, pending))
                {
                    var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                    var message = $"call '{route}' timed out after {timeoutMilliseconds}ms";
                    _diagnostics?.RecordCallTimeout(
                        Endpoint.PluginId,
                        Endpoint.SessionId,
                        Endpoint.EndpointLabel,
                        route,
                        requestId,
                        elapsed,
                        message);
                    throw new RpcCallException(ErrorCode.RequestTimeout, message);
                }
                response = await completion.Task;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (TryAbandon(requestId, pending))
                {
                    throw new RpcCallException(ErrorCode.Cancelled, $"call '{route}' was cancelled");
                }
                response = await completion.Task;
            }

            var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            if (!Routes.IsPing(route))
            {
                _diagnostics?.RecordCallCompleted(
                    Endpoint.PluginId,
                    Endpoint.SessionId,
                    Endpoint.EndpointLabel,
                    route,
                    requestId,
                    elapsedMilliseconds,
                    response.Error is null ? PluginCallOutcome.Success : PluginCallOutcome.Failure,
                    response.Error?.Message);
            }
            if (response.Error is not null)
            {
                throw new RpcCallException(response.Error.Code, response.Error.Message);
            }
            return response.Payload;
        }
        finally
        {
            _pending.TryRemove(new KeyValuePair<string, PendingCall>(requestId, pending));
            UpdatePendingDiagnostics();
            _slots.Release();
        }
    }

    public void FailPending(BusError error)
    {
        foreach (var pair in _pending.ToArray())
        {
            if (!_pending.TryRemove(pair))
            {
                continue;
            }
            _router.AbandonResponseRoute(pair.Key, Endpoint);
            pair.Value.Completion.TrySetException(new RpcCallException(error.Code, error.Message));
        }
        UpdatePendingDiagnostics();
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        FailPending(BusError.For(
            ErrorCode.TransportDisconnected,
            "RPC endpoint disposed",
            retryable: true));
        _router.UnregisterEndpoint(Endpoint);
        _transport.Delivered -= OnDelivery;
        await _transport.DisposeAsync();
    }

    private void OnDelivery(Envelope envelope)
    {
        if (envelope.Kind == MessageKind.Event)
        {
            EventReceived?.Invoke(envelope);
            return;
        }
        if (envelope.Kind != MessageKind.Response
            || envelope.CorrelationId is null
            || !_pending.TryRemove(envelope.CorrelationId, out var pending))
        {
            return;
        }
        UpdatePendingDiagnostics();
        pending.Completion.TrySetResult(envelope);
    }

    private void CompleteWithError(string requestId, PendingCall pending, BusError error)
    {
        if (!_pending.TryRemove(new KeyValuePair<string, PendingCall>(requestId, pending)))
        {
            return;
        }
        _router.AbandonResponseRoute(requestId, Endpoint);
        UpdatePendingDiagnostics();
        pending.Completion.TrySetException(new RpcCallException(error.Code, error.Message));
    }

    private bool TryAbandon(string requestId, PendingCall pending)
    {
        if (!_pending.TryRemove(new KeyValuePair<string, PendingCall>(requestId, pending)))
        {
            return false;
        }
        _router.AbandonResponseRoute(requestId, Endpoint);
        UpdatePendingDiagnostics();
        return true;
    }

    private void UpdatePendingDiagnostics()
        => _diagnostics?.UpdateEndpointPending(
            Endpoint.PluginId,
            Endpoint.SessionId,
            Endpoint.EndpointLabel,
            _pending.Count,
            PendingLimit,
            Volatile.Read(ref _pendingHighWaterMark));

    private void UpdatePendingHighWaterMark()
    {
        var count = _pending.Count;
        var observed = Volatile.Read(ref _pendingHighWaterMark);
        while (count > observed)
        {
            var original = Interlocked.CompareExchange(ref _pendingHighWaterMark, count, observed);
            if (original == observed)
            {
                return;
            }
            observed = original;
        }
    }

    private sealed record PendingCall(
        string Route,
        TaskCompletionSource<Envelope> Completion,
        long StartedAt);
}
