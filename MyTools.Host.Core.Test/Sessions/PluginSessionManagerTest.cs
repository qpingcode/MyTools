using System.Threading.Tasks;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Sessions;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Sessions;

[TestFixture]
public class PluginSessionManagerTest
{
    private static PluginManifestV3 Manifest(string pluginId, string entryId, string nodeEntry = "index.mjs")
        => new()
        {
            Id = pluginId, ProtocolVersion = "3.0",
            Entries = [new() { EntryId = entryId, NodeEntry = nodeEntry, Capabilities = [] }]
        };

    [Test]
    public async Task StartSession_ShouldReachReadyAndRegisterCapabilityManifest()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());
        var manifest = Manifest("settings", "main");

        var session = await mgr.StartSessionAsync(manifest, "main", nodeExePath: "node");

        Assert.That(session.State, Is.EqualTo(SessionState.Ready));
        Assert.That(session.PluginId, Is.EqualTo("settings"));
        Assert.That(session.EntryId, Is.EqualTo("main"));
        Assert.That(session.SessionId, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task StartSession_ShouldAssignUniqueSessionIds()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());

        var s1 = await mgr.StartSessionAsync(Manifest("settings", "main"), "main", "node");
        var s2 = await mgr.StartSessionAsync(Manifest("hello-search", "hello"), "hello", "node");

        Assert.That(s2.SessionId, Is.Not.EqualTo(s1.SessionId));
    }

    [Test]
    public async Task TryGetSession_AfterStart_ShouldReturnTheSession()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());

        var started = await mgr.StartSessionAsync(Manifest("settings", "main"), "main", "node");
        var found = mgr.TryGetSession("settings", "main", started.SessionId, out var retrieved);

        Assert.That(found, Is.True);
        Assert.That(retrieved!.SessionId, Is.EqualTo(started.SessionId));
    }

    [Test]
    public async Task TryGetSession_WithUnknownSession_ShouldReturnFalse()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());

        var found = mgr.TryGetSession("nope", "main", "never", out var retrieved);

        Assert.That(found, Is.False);
        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public async Task StopSession_ShouldTransitionToStopped()
    {
        var mgr = new PluginSessionManager(new MessageBus(), new CapabilityGateway(),
            new FakeProcessControllerFactory());
        var session = await mgr.StartSessionAsync(Manifest("settings", "main"), "main", "node");

        await mgr.StopSessionAsync("settings", "main", session.SessionId);

        Assert.That(session.State, Is.EqualTo(SessionState.Stopped));
        Assert.That(mgr.TryGetSession("settings", "main", session.SessionId, out _), Is.False);
    }
}
