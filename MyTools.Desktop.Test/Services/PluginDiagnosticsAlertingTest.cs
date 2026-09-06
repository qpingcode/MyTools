using Microsoft.Extensions.Logging;
using MyTools.Desktop.Services;
using MyTools.Host.Core.Diagnostics;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class PluginDiagnosticsAlertingTest
{
    [Test]
    public void Evaluate_WhenHeartbeatDeadAndQueuePressureExist_ShouldEmitAlerts()
    {
        var evaluator = new PluginDiagnosticsAlertEvaluator();
        var snapshot = new PluginDiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            1,
            [
                new PluginRuntimeDiagnosticsSnapshot(
                    "settings",
                    "session-1",
                    null,
                    42,
                    null,
                    23,
                    new CounterSnapshot(2, 2),
                    new CounterSnapshot(0, 0),
                    new CounterSnapshot(3, 3),
                    new CounterSnapshot(1, 1),
                    new CounterSnapshot(0, 0),
                    new CounterSnapshot(0, 0),
                    new CounterSnapshot(1, 1),
                    new CounterSnapshot(0, 0),
                    new CounterSnapshot(0, 0),
                    new CounterSnapshot(1, 1),
                    null,
                    [
                        new PluginEndpointDiagnosticsSnapshot(
                            "session-1",
                            "host",
                            1,
                            4,
                            2,
                            4,
                            4,
                            4,
                            1,
                            1,
                            100,
                            1,
                            TimeSpan.FromSeconds(12).TotalMilliseconds,
                            TimeSpan.FromSeconds(12).TotalMilliseconds,
                            0,
                            0)
                    ],
                    [],
                    []),
            ],
            []);

        var alerts = evaluator.Evaluate(snapshot);

        Assert.That(alerts.Select(alert => alert.Key), Is.SupersetOf(new[]
        {
            "settings:heartbeat-dead",
            "settings:disconnects",
            "settings:event-drops",
            "settings:process-exit",
            "settings:queue-pressure"
        }));
    }

    [Test]
    public void Throttler_ShouldSuppressDuplicatesWithinInterval()
    {
        var throttler = new PluginDiagnosticsAlertThrottler(TimeSpan.FromMinutes(2));
        var now = DateTimeOffset.UtcNow;

        Assert.That(throttler.ShouldPublish("settings:heartbeat", now), Is.True);
        Assert.That(throttler.ShouldPublish("settings:heartbeat", now.AddMinutes(1)), Is.False);
        Assert.That(throttler.ShouldPublish("settings:heartbeat", now.AddMinutes(3)), Is.True);
    }
}
