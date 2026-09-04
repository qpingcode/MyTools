using System.Collections.Concurrent;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Bus;

[TestFixture]
public class MessageBusDiagnosticsTest
{
    [Test]
    public async Task RouteRequest_WhenPendingLimitReached_ShouldRecordTooManyRequests()
    {
        var diagnostics = new PluginDiagnosticsService();
        var bus = new MessageBus(new CapabilityGateway(), diagnostics: diagnostics, pendingLimit: 1);
        var nodeTransport = new InMemoryTransport();
        var hostTransport = new InMemoryTransport();
        var nodeEndpoint = new EndpointId("settings", "session-1", "node-main", IsNode: true);
        var hostEndpoint = new EndpointId("settings", "session-1", "host", IsNode: false);
        bus.RegisterEndpoint(nodeEndpoint, nodeTransport);
        bus.RegisterEndpoint(hostEndpoint, hostTransport);

        await bus.RouteRequestAsync(Request("req-1", "plugin.call.search"), hostEndpoint);
        await bus.RouteRequestAsync(Request("req-2", "plugin.call.search"), hostEndpoint);

        var snapshot = diagnostics.GetSnapshot().Plugins.Single(plugin => plugin.PluginId == "settings");
        var call = snapshot.CallMetrics.Single(metric => metric.Route == "plugin.call.search");
        var endpoint = snapshot.Endpoints.Single(metric => metric.EndpointId == "host");

        Assert.Multiple(() =>
        {
            Assert.That(call.CallCount, Is.EqualTo(1));
            Assert.That(call.RejectedCount, Is.EqualTo(1));
            Assert.That(snapshot.TooManyRequests.Total, Is.EqualTo(1));
            Assert.That(endpoint.PendingInFlight, Is.EqualTo(1));
            Assert.That(endpoint.TooManyRequestsTotal, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BroadcastEvent_WhenQueueOverflows_ShouldRecordDropsAndDeliveryLatency()
    {
        var diagnostics = new PluginDiagnosticsService();
        var bus = new MessageBus(new CapabilityGateway(), diagnostics: diagnostics, eventQueueCapacity: 1);
        var nodeTransport = new InMemoryTransport();
        var slowTransport = new BlockingTransport();
        var nodeEndpoint = new EndpointId("settings", "session-1", "node-main", IsNode: true);
        var webEndpoint = new EndpointId("settings", "session-1", "web", IsNode: false);
        bus.RegisterEndpoint(nodeEndpoint, nodeTransport);
        bus.RegisterEndpoint(webEndpoint, slowTransport);

        nodeTransport.Deliver(Event("evt-1"));
        await slowTransport.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        nodeTransport.Deliver(Event("evt-2"));
        nodeTransport.Deliver(Event("evt-3"));
        slowTransport.Release();
        await WaitForAsync(() => slowTransport.DeliveredRoutes.Count >= 2);

        var snapshot = diagnostics.GetSnapshot().Plugins.Single(plugin => plugin.PluginId == "settings");
        var endpoint = snapshot.Endpoints.Single(metric => metric.EndpointId == "web");
        var eventMetric = snapshot.EventMetrics.Single(metric => metric.Route == "plugin.event.changed");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.EventQueueDrops.Total, Is.EqualTo(1));
            Assert.That(endpoint.EventQueueDroppedTotal, Is.EqualTo(1));
            Assert.That(eventMetric.EventCount, Is.EqualTo(3));
            Assert.That(eventMetric.Delivery.TotalCount, Is.GreaterThanOrEqualTo(2));
        });
    }

    private static Envelope Request(string id, string route) => new()
    {
        Version = ProtocolVersion.Current,
        Id = id,
        TraceId = id,
        SessionId = "session-1",
        PluginId = "settings",
        EndpointId = "host",
        Kind = MessageKind.Request,
        Route = route,
        TimeoutMs = 5000
    };

    private static Envelope Event(string id) => new()
    {
        Version = ProtocolVersion.Current,
        Id = id,
        TraceId = id,
        SessionId = "session-1",
        PluginId = "settings",
        EndpointId = "node-main",
        Kind = MessageKind.Event,
        Route = "plugin.event.changed"
    };

    private static async Task<bool> WaitForAsync(Func<bool> predicate, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return predicate();
    }

    private sealed class BlockingTransport : IMessageTransport
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _sendCount;

        public bool IsConnected => true;
        public ConcurrentQueue<string> DeliveredRoutes { get; } = new();
        public TaskCompletionSource FirstSendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event Action<Envelope>? MessageReceived
        {
            add { }
            remove { }
        }

        public event Action? Disconnected
        {
            add { }
            remove { }
        }

        public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken)
        {
            DeliveredRoutes.Enqueue(envelope.Route);
            if (Interlocked.Increment(ref _sendCount) == 1)
            {
                FirstSendStarted.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
        }

        public void Release() => _release.TrySetResult();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
