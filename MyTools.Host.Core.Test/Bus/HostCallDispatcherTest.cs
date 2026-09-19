using System.Text.Json;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Manifest;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Bus;

[TestFixture]
public class HostCallDispatcherTest
{
    private static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(2);
    private const string PluginId = "settings";
    private const string SessionId = "session-1";
    private const string Capability = "configuration.read";
    private const string HostCallRoute = "host.call.configuration.read";

    [Test]
    public async Task Dispatch_HandlerOutlivesCallerTimeoutBudget()
    {
        var gateway = new CapabilityGateway();
        gateway.RegisterManifest(new PluginManifest(PluginId, [Capability]));
        var bus = new MessageBus(gateway);
        var node = new InMemoryTransport();
        var nodeEndpoint = Node();
        bus.RegisterEndpoint(nodeEndpoint, node);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bus.HostCallDispatcher.RegisterHandler(PluginId, (_, _, _) =>
        {
            handlerStarted.TrySetResult();
            return releaseHandler.Task;
        });

        node.Deliver(Request(nodeEndpoint, timeoutMilliseconds: 1));
        await handlerStarted.Task.WaitAsync(AssertionTimeout);
        await Task.Delay(30);
        Assert.That(node.Sent, Is.Empty);

        releaseHandler.TrySetResult(JsonDocument.Parse("""{"ok":true}""").RootElement.Clone());
        await WaitForReplyAsync(node);

        var response = node.Sent.ToArray()[^1];
        Assert.Multiple(() =>
        {
            Assert.That(response.Error, Is.Null);
            Assert.That(response.Payload?["ok"]?.GetValue<bool>(), Is.True);
        });
    }

    [Test]
    public async Task Dispatch_UndeclaredCapability_ShouldSkipHandler()
    {
        var gateway = new CapabilityGateway();
        gateway.RegisterManifest(new PluginManifest(PluginId, []));
        var bus = new MessageBus(gateway);
        var node = new InMemoryTransport();
        bus.RegisterEndpoint(Node(), node);
        var handlerCalled = false;
        bus.HostCallDispatcher.RegisterHandler(PluginId, (_, _, _) =>
        {
            handlerCalled = true;
            return Task.FromResult(JsonDocument.Parse("{}").RootElement.Clone());
        });

        node.Deliver(Request(Node(), timeoutMilliseconds: 5000));
        await WaitForReplyAsync(node);

        Assert.Multiple(() =>
        {
            Assert.That(handlerCalled, Is.False);
            Assert.That(node.Sent.ToArray()[^1].Error?.Code, Is.EqualTo(ErrorCode.CapabilityNotDeclared));
        });
    }

    [Test]
    public async Task Dispatch_WebViewHostCall_ShouldBeRejectedBeforeHandler()
    {
        var gateway = new CapabilityGateway();
        gateway.RegisterManifest(new PluginManifest(PluginId, [Capability]));
        var bus = new MessageBus(gateway);
        var web = new InMemoryTransport();
        var webEndpoint = new EndpointId(PluginId, SessionId, "web", IsNode: false);
        bus.RegisterEndpoint(webEndpoint, web);
        var handlerCalled = false;
        bus.HostCallDispatcher.RegisterHandler(PluginId, (_, _, _) =>
        {
            handlerCalled = true;
            return Task.FromResult(JsonDocument.Parse("{}").RootElement.Clone());
        });

        web.Deliver(Request(webEndpoint, timeoutMilliseconds: 5000));
        await WaitForReplyAsync(web);

        Assert.Multiple(() =>
        {
            Assert.That(handlerCalled, Is.False);
            Assert.That(web.Sent.ToArray()[^1].Error?.Code, Is.EqualTo(ErrorCode.CapabilityDenied));
        });
    }

    private static EndpointId Node()
        => new(PluginId, SessionId, EndpointIds.NodeMain, IsNode: true);

    private static Envelope Request(EndpointId source, int timeoutMilliseconds)
        => new()
        {
            Version = ProtocolVersion.Current,
            Id = "host-call-1",
            TraceId = "host-call-1",
            Kind = MessageKind.Request,
            Route = HostCallRoute,
            TimeoutMs = timeoutMilliseconds,
        };

    private static async Task WaitForReplyAsync(InMemoryTransport transport)
    {
        var deadline = Environment.TickCount64 + (long)AssertionTimeout.TotalMilliseconds;
        while (transport.Sent.IsEmpty && Environment.TickCount64 < deadline)
        {
            await Task.Delay(5);
        }
        Assert.That(transport.Sent, Is.Not.Empty);
    }
}
