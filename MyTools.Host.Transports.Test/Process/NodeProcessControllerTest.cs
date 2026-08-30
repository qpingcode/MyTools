using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Transports.NamedPipe;
using MyTools.Host.Transports.Process;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Handshake;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Transports.Test.Process;

/// <summary>
/// NodeProcessController integration tests: spawn a real node script, complete bus.handshake,
/// send an envelope over the pipe, and verify the round trip.
/// </summary>
[TestFixture]
[Category("Integration")]
public class NodeProcessControllerTest
{
    private static Envelope Ping(string id, string sessionId, string pluginId) => new()
    {
        Version = ProtocolVersion.Current, Id = id, TraceId = id, SessionId = sessionId,
        PluginId = pluginId, EndpointId = "node-main",
        Kind = MessageKind.Request, Route = "bus.ping", TimeoutMs = 5000
    };

    private static string SdkV3EntryPath()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = System.IO.Path.Combine(dir, "MyTools.Plugins", "Examples", "sdk-v3",
                "test-fixture-entry.mjs");
            if (System.IO.File.Exists(candidate)) return candidate;
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }
        Assert.Fail("sdk-v3 test-fixture-entry.mjs not found");
        return null!;
    }

    private static string SdkV3CrashEntryPath()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = System.IO.Path.Combine(dir, "MyTools.Plugins", "Examples", "sdk-v3",
                "test-fixture-crash-entry.mjs");
            if (System.IO.File.Exists(candidate)) return candidate;
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }
        Assert.Fail("sdk-v3 test-fixture-crash-entry.mjs not found");
        return null!;
    }

    [Test]
    public async Task Start_SpawnsNode_Handshakes_RoundTripsBusPing()
    {
        var entry = SdkV3EntryPath();
        var pluginsDataRoot = TestPluginsDataRoot();
        var controller = new NodeProcessController("node", entry, pluginsDataRoot);
        var tokens = new BootstrapTokenValidator();
        var ids = new GuidIdGenerator();
        const string pluginId = "fixture";
        const string sessionId = "sess-it-1";
        const string endpointId = "node-main";

        await controller.StartAsync(
            $"mytools-it-{System.Guid.NewGuid():N}",
            pluginId,
            identity => tokens.Issue(identity, TimeSpan.FromSeconds(30)).Value,
            default);

        Assert.That(controller.Transport, Is.Not.Null);
        Assert.That(controller.ObservedIdentity, Is.Not.Null);
        Assert.That(Directory.Exists(pluginsDataRoot), Is.False,
            "starting a plugin must not create the pluginsData root");
        Assert.That(Directory.Exists(Path.Combine(pluginsDataRoot, pluginId)), Is.False,
            "starting a plugin must not create an empty per-plugin data directory");

        await PipeHandshake.CompleteAsHostAsync(
            controller.Transport!,
            tokens,
            controller.ObservedIdentity!,
            sessionId,
            endpointId,
            ids,
            TimeSpan.FromSeconds(10),
            default);

        Envelope? response = null;
        controller.Transport!.MessageReceived += e =>
        {
            if (e.Kind == MessageKind.Response && e.CorrelationId == "ping-1") response = e;
        };
        await controller.Transport.SendAsync(Ping("ping-1", sessionId, pluginId), default);

        for (var i = 0; i < 100 && response is null; i++) await Task.Delay(50);

        await controller.StopAsync();

        Assert.That(response, Is.Not.Null, "did not receive bus.ping reply within timeout");
        Assert.That(response!.CorrelationId, Is.EqualTo("ping-1"));
    }

    [Test]
    public void Stop_WhenNotStarted_ShouldNotThrow()
    {
        var controller = new NodeProcessController("node", "placeholder.mjs", TestPluginsDataRoot());
        Assert.DoesNotThrowAsync(async () => await controller.StopAsync());
    }

    [Test]
    public void Transport_BeforeStart_ShouldBeNull()
    {
        var controller = new NodeProcessController("node", "placeholder.mjs", TestPluginsDataRoot());
        Assert.That(controller.Transport, Is.Null);
    }

    [Test]
    public async Task Crash_AfterHandshake_CapturesStderrAndExitCode()
    {
        var controller = new NodeProcessController("node", SdkV3CrashEntryPath(), TestPluginsDataRoot());
        var tokens = new BootstrapTokenValidator();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await controller.StartAsync(
            $"mytools-it-{System.Guid.NewGuid():N}",
            "crash-fixture",
            identity => tokens.Issue(identity, TimeSpan.FromSeconds(30)).Value,
            default);
        controller.Transport!.Disconnected += () => disconnected.TrySetResult();

        await PipeHandshake.CompleteAsHostAsync(
            controller.Transport,
            tokens,
            controller.ObservedIdentity!,
            "sess-crash",
            "node-main",
            new GuidIdGenerator(),
            TimeSpan.FromSeconds(10),
            default);

        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 100 && controller.FailureDetails?.Contains("code 23") != true; i++)
        {
            await Task.Delay(20);
        }

        Assert.That(controller.FailureDetails, Does.Contain("[stderr] fixture backend crashed"));
        Assert.That(controller.FailureDetails, Does.Contain("Node process exited with code 23"));
        await controller.StopAsync();
    }

    private static string TestPluginsDataRoot()
    {
        return System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "MyTools.Host.Transports.Test",
            $"pluginsData-{Guid.NewGuid():N}");
    }
}
