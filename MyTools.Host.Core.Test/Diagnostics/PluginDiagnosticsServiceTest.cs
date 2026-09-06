using Microsoft.Extensions.Logging;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Diagnostics;

[TestFixture]
public class PluginDiagnosticsServiceTest
{
    [Test]
    public async Task AttachProcessController_ShouldCaptureBoundedSamples()
    {
        using var diagnostics = new PluginDiagnosticsService();
        var controller = new SamplingController();
        diagnostics.RecordSessionState("settings", "session-1", SessionState.Ready, pid: 42);
        diagnostics.RecordDiagnostic(LogLevel.Information, "test", "first", "settings", "session-1");
        diagnostics.RecordDiagnostic(LogLevel.Warning, "test", "second", "settings", "session-1");
        diagnostics.AttachProcessController("settings", "session-1", controller);

        controller.ResourceUsage = new NodeProcessResourceUsage(
            42,
            100 * 1024 * 1024,
            80 * 1024 * 1024,
            TimeSpan.FromMilliseconds(100),
            DateTimeOffset.UtcNow);
        await Task.Delay(TimeSpan.FromSeconds(2.2));

        controller.ResourceUsage = new NodeProcessResourceUsage(
            42,
            120 * 1024 * 1024,
            90 * 1024 * 1024,
            TimeSpan.FromMilliseconds(600),
            DateTimeOffset.UtcNow);
        await Task.Delay(TimeSpan.FromSeconds(2.2));

        var all = diagnostics.GetSnapshot();
        var snapshot = all.Plugins.Single(plugin => plugin.PluginId == "settings");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Process, Is.Not.Null);
            Assert.That(snapshot.Process!.Pid, Is.EqualTo(42));
            Assert.That(snapshot.Process.WorkingSetBytes, Is.EqualTo(120L * 1024 * 1024));
            Assert.That(snapshot.Process.CpuPercent, Is.GreaterThan(0));
            Assert.That(all.Records.First().Sequence, Is.LessThan(all.Records.Last().Sequence));
        });
    }

    [Test]
    public async Task RecordCallMetrics_ShouldRemainExactUnderParallelTraffic()
    {
        using var diagnostics = new PluginDiagnosticsService();
        diagnostics.RecordSessionState("settings", "session-1", SessionState.Ready, pid: 42);

        const int successCount = 140;
        const int failureCount = 90;
        const int timeoutCount = 70;
        const int rejectedCount = 50;
        const string endpointId = "host";
        const string route = "plugin/search";
        using var start = new ManualResetEventSlim(false);
        var stopSnapshots = 0;
        var snapshotReads = 0;

        var snapshotTask = Task.Run(() =>
        {
            start.Wait();
            while (Volatile.Read(ref stopSnapshots) == 0)
            {
                _ = diagnostics.GetSnapshot();
                Interlocked.Increment(ref snapshotReads);
                Thread.Yield();
            }
        });

        var producers = Enumerable.Range(0, successCount)
            .Select(index => Task.Run(() =>
            {
                start.Wait();
                diagnostics.RecordCallCompleted(
                    "settings",
                    "session-1",
                    endpointId,
                    route,
                    $"ok-{index}",
                    125,
                    PluginCallOutcome.Success);
            }))
            .Concat(Enumerable.Range(0, failureCount)
                .Select(index => Task.Run(() =>
                {
                    start.Wait();
                    diagnostics.RecordCallCompleted(
                        "settings",
                        "session-1",
                        endpointId,
                        route,
                        $"fail-{index}",
                        2_500,
                        PluginCallOutcome.Failure,
                        "boom");
                })))
            .Concat(Enumerable.Range(0, timeoutCount)
                .Select(index => Task.Run(() =>
                {
                    start.Wait();
                    diagnostics.RecordCallTimeout(
                        "settings",
                        "session-1",
                        endpointId,
                        route,
                        $"timeout-{index}",
                        3_000,
                        "timed out");
                })))
            .Concat(Enumerable.Range(0, rejectedCount)
                .Select(index => Task.Run(() =>
                {
                    start.Wait();
                    diagnostics.RecordCallRejected(
                        "settings",
                        "session-1",
                        endpointId,
                        route,
                        $"reject-{index}",
                        "queue full");
                })))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers);
        Volatile.Write(ref stopSnapshots, 1);
        await snapshotTask;

        var plugin = diagnostics.GetSnapshot().Plugins.Single(entry => entry.PluginId == "settings");
        var endpoint = plugin.Endpoints.Single(entry => entry.EndpointId == endpointId);
        var call = plugin.CallMetrics.Single(entry => entry.EndpointId == endpointId && entry.Route == route);

        Assert.Multiple(() =>
        {
            Assert.That(snapshotReads, Is.GreaterThan(0));
            Assert.That(call.CallCount, Is.EqualTo(successCount + failureCount + timeoutCount + rejectedCount));
            Assert.That(call.SuccessCount, Is.EqualTo(successCount));
            Assert.That(call.FailureCount, Is.EqualTo(failureCount));
            Assert.That(call.TimeoutCount, Is.EqualTo(timeoutCount));
            Assert.That(call.RejectedCount, Is.EqualTo(rejectedCount));
            Assert.That(call.RecentSlowCount, Is.EqualTo(failureCount + timeoutCount));
            Assert.That(call.Latency.TotalCount, Is.EqualTo(successCount + failureCount + timeoutCount));
            Assert.That(call.Latency.SampleCount, Is.EqualTo(256));
            Assert.That(call.RecentDetails, Has.Count.EqualTo(32));
            Assert.That(plugin.Errors.Total, Is.EqualTo(failureCount));
            Assert.That(plugin.RequestTimeouts.Total, Is.EqualTo(timeoutCount));
            Assert.That(plugin.TooManyRequests.Total, Is.EqualTo(rejectedCount));
            Assert.That(endpoint.TooManyRequestsTotal, Is.EqualTo(rejectedCount));
            Assert.That(endpoint.TooManyRequestsRecent, Is.EqualTo(rejectedCount));
        });
    }

    [Test]
    public async Task RecordEventMetrics_ShouldRemainExactUnderParallelTraffic()
    {
        using var diagnostics = new PluginDiagnosticsService();
        diagnostics.RecordSessionState("settings", "session-1", SessionState.Ready, pid: 42);

        const int eventCount = 320;
        const string endpointId = "events";
        const string route = "plugin/event";
        using var start = new ManualResetEventSlim(false);
        var stopSnapshots = 0;
        var snapshotReads = 0;

        var snapshotTask = Task.Run(() =>
        {
            start.Wait();
            while (Volatile.Read(ref stopSnapshots) == 0)
            {
                _ = diagnostics.GetSnapshot();
                Interlocked.Increment(ref snapshotReads);
                Thread.Yield();
            }
        });

        var producers = Enumerable.Range(0, eventCount)
            .Select(index => Task.Run(() =>
            {
                start.Wait();
                diagnostics.RecordEventQueued(
                    "settings",
                    "session-1",
                    endpointId,
                    route,
                    depth: 1,
                    capacity: 8,
                    highWaterMark: 6,
                    droppedTotal: 0,
                    dropped: false,
                    oldestWaitMs: 11);
                diagnostics.RecordEventDelivered(
                    "settings",
                    "session-1",
                    endpointId,
                    route,
                    queueWaitMs: 20 + (index % 5),
                    deliveryMs: 40 + (index % 7),
                    depth: 1,
                    capacity: 8,
                    highWaterMark: 6,
                    droppedTotal: 0,
                    oldestWaitMs: 11);
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers);
        Volatile.Write(ref stopSnapshots, 1);
        await snapshotTask;

        var plugin = diagnostics.GetSnapshot().Plugins.Single(entry => entry.PluginId == "settings");
        var endpoint = plugin.Endpoints.Single(entry => entry.EndpointId == endpointId);
        var metric = plugin.EventMetrics.Single(entry => entry.EndpointId == endpointId && entry.Route == route);

        Assert.Multiple(() =>
        {
            Assert.That(snapshotReads, Is.GreaterThan(0));
            Assert.That(metric.EventCount, Is.EqualTo(eventCount));
            Assert.That(metric.QueueWait.TotalCount, Is.EqualTo(eventCount));
            Assert.That(metric.QueueWait.SampleCount, Is.EqualTo(256));
            Assert.That(metric.Delivery.TotalCount, Is.EqualTo(eventCount));
            Assert.That(metric.Delivery.SampleCount, Is.EqualTo(256));
            Assert.That(endpoint.EventQueueDepth, Is.EqualTo(1));
            Assert.That(endpoint.EventQueueCapacity, Is.EqualTo(8));
            Assert.That(endpoint.EventQueueHighWater, Is.EqualTo(6));
            Assert.That(endpoint.EventQueueOldestWaitMs, Is.EqualTo(11));
        });
    }

    [Test]
    public void RecordCallTimeout_ShouldContributeLatencyAndSlowCount()
    {
        using var diagnostics = new PluginDiagnosticsService();
        diagnostics.RecordSessionState("settings", "session-1", SessionState.Ready, pid: 42);

        diagnostics.RecordCallCompleted(
            "settings",
            "session-1",
            "host",
            "plugin/search",
            "ok-1",
            100,
            PluginCallOutcome.Success);
        diagnostics.RecordCallTimeout(
            "settings",
            "session-1",
            "host",
            "plugin/search",
            "timeout-1",
            3_000,
            "deadline exceeded");

        var plugin = diagnostics.GetSnapshot().Plugins.Single(entry => entry.PluginId == "settings");
        var call = plugin.CallMetrics.Single(entry => entry.EndpointId == "host" && entry.Route == "plugin/search");

        Assert.Multiple(() =>
        {
            Assert.That(call.CallCount, Is.EqualTo(2));
            Assert.That(call.TimeoutCount, Is.EqualTo(1));
            Assert.That(call.RecentSlowCount, Is.EqualTo(1));
            Assert.That(call.Latency.TotalCount, Is.EqualTo(2));
            Assert.That(call.Latency.SampleCount, Is.EqualTo(2));
            Assert.That(call.Latency.RecentMs, Is.EqualTo(3_000));
            Assert.That(call.Latency.AverageMs, Is.EqualTo(1_550).Within(0.001));
            Assert.That(call.Latency.MaxMs, Is.EqualTo(3_000));
            Assert.That(call.Latency.P50Ms, Is.EqualTo(100));
            Assert.That(call.Latency.P95Ms, Is.EqualTo(3_000));
            Assert.That(call.Latency.P99Ms, Is.EqualTo(3_000));
            Assert.That(call.RecentDetails.Single().Outcome, Is.EqualTo("timeout"));
            Assert.That(call.RecentDetails.Single().ElapsedMs, Is.EqualTo(3_000));
            Assert.That(plugin.RequestTimeouts.Total, Is.EqualTo(1));
        });
    }

    private sealed class SamplingController : INodeProcessController
    {
        public IMessageTransport? Transport => null;
        public ProcessIdentity? ObservedIdentity => null;
        public NodeProcessResourceUsage? ResourceUsage { get; set; }
        public event Action<NodeProcessExitInfo>? ProcessExited
        {
            add { }
            remove { }
        }

        public Task StartAsync(string pipeName, string pluginId, Func<ProcessIdentity, string> issueToken, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public NodeProcessResourceUsage? TryGetResourceUsage() => ResourceUsage;
    }
}
