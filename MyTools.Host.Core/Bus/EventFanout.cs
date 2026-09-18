using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Host.Core.Backpressure;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Messages;

namespace MyTools.Host.Core.Bus;

/// <summary>Owns bounded per-endpoint event queues and their delivery diagnostics.</summary>
public sealed class EventFanout
{
    public const int DefaultQueueCapacity = 64;

    private readonly object _gate = new();
    private readonly Dictionary<EndpointId, EndpointQueue> _endpoints = new();
    private readonly IPluginDiagnosticsService? _diagnostics;
    private readonly ILogger _logger;
    private readonly int _capacity;

    public EventFanout(
        IPluginDiagnosticsService? diagnostics = null,
        ILogger? logger = null,
        int queueCapacity = DefaultQueueCapacity)
    {
        _diagnostics = diagnostics;
        _logger = logger ?? NullLogger.Instance;
        _capacity = queueCapacity;
    }

    public long TotalDroppedEvents
    {
        get
        {
            lock (_gate)
            {
                return _endpoints.Values.Sum(endpoint => endpoint.Queue.DroppedEvents);
            }
        }
    }

    public void RegisterEndpoint(EndpointId endpoint, IMessageTransport transport)
    {
        lock (_gate)
        {
            _endpoints[endpoint] = new EndpointQueue(
                endpoint,
                transport,
                new BoundedEventQueue<QueuedEvent>(_capacity));
            UpdateState(_endpoints[endpoint]);
        }
    }

    public void UnregisterEndpoint(EndpointId endpoint)
    {
        lock (_gate)
        {
            _endpoints.Remove(endpoint);
        }
    }

    public Task BroadcastAsync(
        EndpointId sessionEndpoint,
        Envelope envelope,
        string? excludeEndpointId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<EndpointQueue> toDrain = [];
        lock (_gate)
        {
            foreach (var endpoint in _endpoints.Values)
            {
                if (endpoint.Id.PluginId != sessionEndpoint.PluginId
                    || endpoint.Id.SessionId != sessionEndpoint.SessionId
                    || endpoint.Id.EndpointLabel == excludeEndpointId)
                {
                    continue;
                }

                var dropped = endpoint.Queue.TryEnqueue(
                    new QueuedEvent(envelope, Stopwatch.GetTimestamp()),
                    out var droppedEvent);
                _diagnostics?.RecordEventQueued(
                    endpoint.Id.PluginId,
                    endpoint.Id.SessionId,
                    endpoint.Id.EndpointLabel,
                    envelope.Route,
                    endpoint.Queue.Count,
                    endpoint.Queue.Capacity,
                    endpoint.Queue.HighWaterMark,
                    endpoint.Queue.DroppedEvents,
                    dropped,
                    GetOldestWaitMilliseconds(endpoint.Queue),
                    dropped ? droppedEvent.Envelope.Route : null);

                if (!endpoint.IsDraining)
                {
                    endpoint.IsDraining = true;
                    toDrain.Add(endpoint);
                }
            }
        }

        foreach (var endpoint in toDrain)
        {
            _ = DrainAsync(endpoint);
        }

        return Task.CompletedTask;
    }

    private async Task DrainAsync(EndpointQueue endpoint)
    {
        try
        {
            while (true)
            {
                IReadOnlyList<QueuedEvent> batch;
                lock (_gate)
                {
                    if (!_endpoints.TryGetValue(endpoint.Id, out var current)
                        || !ReferenceEquals(current, endpoint))
                    {
                        return;
                    }

                    batch = endpoint.Queue.Drain();
                    UpdateState(endpoint);
                    if (batch.Count == 0)
                    {
                        endpoint.IsDraining = false;
                        return;
                    }
                }

                foreach (var queued in batch)
                {
                    var queueWait = Stopwatch.GetElapsedTime(queued.EnqueuedAt).TotalMilliseconds;
                    await endpoint.Transport.SendAsync(queued.Envelope, CancellationToken.None);
                    var delivery = Stopwatch.GetElapsedTime(queued.EnqueuedAt).TotalMilliseconds;
                    lock (_gate)
                    {
                        _diagnostics?.RecordEventDelivered(
                            endpoint.Id.PluginId,
                            endpoint.Id.SessionId,
                            endpoint.Id.EndpointLabel,
                            queued.Envelope.Route,
                            queueWait,
                            delivery,
                            endpoint.Queue.Count,
                            endpoint.Queue.Capacity,
                            endpoint.Queue.HighWaterMark,
                            endpoint.Queue.DroppedEvents,
                            GetOldestWaitMilliseconds(endpoint.Queue));
                    }
                }
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                endpoint.IsDraining = false;
            }
            _logger.LogDebug(exception,
                "Event delivery stopped plugin={PluginId} session={SessionId} endpoint={Endpoint}",
                endpoint.Id.PluginId,
                endpoint.Id.SessionId,
                endpoint.Id.EndpointLabel);
        }
    }

    private void UpdateState(EndpointQueue endpoint)
        => _diagnostics?.UpdateEventQueueState(
            endpoint.Id.PluginId,
            endpoint.Id.SessionId,
            endpoint.Id.EndpointLabel,
            endpoint.Queue.Count,
            endpoint.Queue.Capacity,
            endpoint.Queue.HighWaterMark,
            endpoint.Queue.DroppedEvents,
            GetOldestWaitMilliseconds(endpoint.Queue));

    private static double GetOldestWaitMilliseconds(BoundedEventQueue<QueuedEvent> queue)
        => queue.TryPeek(out var queued)
            ? Stopwatch.GetElapsedTime(queued.EnqueuedAt).TotalMilliseconds
            : 0;

    private readonly record struct QueuedEvent(Envelope Envelope, long EnqueuedAt);

    private sealed record EndpointQueue(
        EndpointId Id,
        IMessageTransport Transport,
        BoundedEventQueue<QueuedEvent> Queue)
    {
        public bool IsDraining { get; set; }
    }
}
