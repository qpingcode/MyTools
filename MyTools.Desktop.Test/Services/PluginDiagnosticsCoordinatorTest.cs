using MyTools.Desktop.Services;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Sessions;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class PluginDiagnosticsCoordinatorTest
{
    [Test]
    public void GetSnapshot_WhenNodePluginHasNoLiveSession_PreservesStoppedSnapshot()
    {
        var diagnostics = new StaticPluginDiagnosticsService(
            new PluginDiagnosticsSnapshot(
                DateTimeOffset.UtcNow,
                12,
                [
                    new PluginRuntimeDiagnosticsSnapshot(
                        "settings",
                        "session-1",
                        SessionState.Stopped,
                        42,
                        "process exited",
                        23,
                        new CounterSnapshot(1, 1),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(1, 1),
                        new CounterSnapshot(1, 1),
                        new CounterSnapshot(0, 0),
                        new CounterSnapshot(0, 0),
                        null,
                        [],
                        [],
                        [])
                ],
                []));

        using var nodePlugin = PluginDiagnosticsTestHelper.CreateNodePlugin("settings", diagnostics);
        using var loader = PluginDiagnosticsTestHelper.CreatePluginLoader(nodePlugin, diagnostics);
        var coordinator = new PluginDiagnosticsCoordinator(loader, diagnostics);

        var (plugins, _) = coordinator.GetSnapshot();
        var plugin = plugins.Single();

        Assert.Multiple(() =>
        {
            Assert.That(nodePlugin.BusSessionId, Is.Null);
            Assert.That(plugin.RuntimeSnapshot, Is.Not.Null);
            Assert.That(plugin.RuntimeSnapshot!.CurrentSessionId, Is.EqualTo("session-1"));
            Assert.That(plugin.RuntimeSnapshot.SessionState, Is.EqualTo(SessionState.Stopped));
            Assert.That(plugin.RuntimeSnapshot.LastExitCode, Is.EqualTo(23));
        });
    }
}
