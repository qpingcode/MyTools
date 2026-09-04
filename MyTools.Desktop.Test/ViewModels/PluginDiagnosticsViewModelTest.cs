using MyTools.Desktop.Services;
using MyTools.Desktop.ViewModels;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Sessions;
using NUnit.Framework;

namespace MyTools.Desktop.Test.ViewModels;

[TestFixture]
public class PluginDiagnosticsViewModelTest
{
    [Test]
    public void RefreshDetails_ShouldFormatCompactCallCounts()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var routeADetails = Enumerable.Range(0, 60)
            .Select(index => new PluginOperationDetailSnapshot(
                index + 1,
                capturedAt.AddSeconds(-120 + index),
                $"corr-a-{index}",
                "plugin/route-a",
                "failure",
                2_500,
                $"detail-a-{index}"))
            .ToArray();
        var routeBDetails = Enumerable.Range(0, 60)
            .Select(index => new PluginOperationDetailSnapshot(
                1_000 + index,
                capturedAt.AddSeconds(-60 + index),
                $"corr-b-{index}",
                "plugin/route-b",
                index == 59 ? "rejected" : "timeout",
                index == 59 ? 0 : 3_000,
                index == 59 ? "queue full" : $"detail-b-{index}"))
            .ToArray();
        var diagnostics = new StaticPluginDiagnosticsService(
            new PluginDiagnosticsSnapshot(
                capturedAt,
                500,
                [
                    new PluginRuntimeDiagnosticsSnapshot(
                        "settings",
                        "session-9",
                        SessionState.Stopped,
                        42,
                        null,
                        0,
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(1, 1),
                        new CounterSnapshot(2, 2),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(3, 3),
                        new CounterSnapshot(0, 0),
                        null,
                        [],
                        [
                            new PluginCallMetricsSnapshot(
                                "session-9",
                                "host",
                                "plugin/route-a",
                                60,
                                0,
                                60,
                                0,
                                0,
                                60,
                                new LatencySnapshot(60, 60, 2_500, 2_500, 2_500, 2_500, 2_500, 2_500),
                                routeADetails),
                            new PluginCallMetricsSnapshot(
                                "session-9",
                                "host",
                                "plugin/route-b",
                                60,
                                0,
                                0,
                                59,
                                1,
                                59,
                                new LatencySnapshot(59, 59, 3_000, 3_000, 3_000, 3_000, 3_000, 3_000),
                                routeBDetails)
                        ],
                        [])
                ],
                []));

        using var nodePlugin = PluginDiagnosticsTestHelper.CreateNodePlugin("settings", diagnostics);
        using var loader = PluginDiagnosticsTestHelper.CreatePluginLoader(nodePlugin, diagnostics);
        var coordinator = new PluginDiagnosticsCoordinator(loader, diagnostics);
        using var viewModel = new PluginDiagnosticsViewModel(coordinator, diagnostics, PluginDiagnosticsTestHelper.Localization);

        var routeA = viewModel.CallMetrics.Single(metric => metric.Route == "plugin/route-a");
        var routeB = viewModel.CallMetrics.Single(metric => metric.Route == "plugin/route-b");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedPluginId, Is.EqualTo("settings"));
            Assert.That(routeA.Counts, Is.EqualTo("60/0/60/0/0"));
            Assert.That(routeB.Counts, Is.EqualTo("60/0/0/59/1"));
        });
    }
}
