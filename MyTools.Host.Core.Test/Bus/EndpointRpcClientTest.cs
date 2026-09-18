using System.Text.Json.Nodes;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Bus;

[TestFixture]
public class EndpointRpcClientTest
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(40);

    [Test]
    public async Task CallAsync_Response_ShouldCompleteAndReleasePending()
    {
        var bus = new MessageBus();
        var node = new InMemoryTransport();
        var nodeEndpoint = Node("session-1");
        bus.RegisterEndpoint(nodeEndpoint, node);
        await using var client = Client(bus, "session-1", EndpointIds.Host);

        var call = client.CallAsync(
            "plugin.call.search",
            JsonNode.Parse("""{"query":"x"}"""),
            TestTimeout,
            CancellationToken.None);
        var request = await WaitForSentAsync(node);
        node.Deliver(Response(nodeEndpoint, request, JsonNode.Parse("""{"ok":true}""")));

        var result = await call;

        Assert.Multiple(() =>
        {
            Assert.That(result?["ok"]?.GetValue<bool>(), Is.True);
            Assert.That(client.PendingCount, Is.Zero);
            Assert.That(bus.ResponseRouteCount, Is.Zero);
        });
    }

    [Test]
    public async Task CallAsync_Timeout_ShouldAbandonRouteAndDropLateResponse()
    {
        var bus = new MessageBus();
        var node = new InMemoryTransport();
        var nodeEndpoint = Node("session-1");
        bus.RegisterEndpoint(nodeEndpoint, node);
        await using var client = Client(bus, "session-1", EndpointIds.Host);

        var call = client.CallAsync(
            "plugin.call.search",
            payload: null,
            ShortTimeout,
            CancellationToken.None);
        var request = await WaitForSentAsync(node);

        var exception = Assert.ThrowsAsync<RpcCallException>(async () => await call);
        node.Deliver(Response(nodeEndpoint, request, payload: null));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(ErrorCode.RequestTimeout));
            Assert.That(client.PendingCount, Is.Zero);
            Assert.That(bus.ResponseRouteCount, Is.Zero);
        });
    }

    [Test]
    public async Task IndependentClients_WhenBusinessLimitReached_ControlCallStillRoutes()
    {
        var bus = new MessageBus();
        var node = new InMemoryTransport();
        bus.RegisterEndpoint(Node("session-1"), node);
        await using var business = Client(bus, "session-1", EndpointIds.Host, pendingLimit: 1);
        await using var control = Client(bus, "session-1", EndpointIds.HostControl, pendingLimit: 1);
        using var cancellation = new CancellationTokenSource();

        var occupied = business.CallAsync(
            "plugin.call.search",
            payload: null,
            TestTimeout,
            cancellation.Token);
        await WaitForSentCountAsync(node, 1);

        var rejected = Assert.ThrowsAsync<RpcCallException>(async () =>
            await business.CallAsync(
                "plugin.call.search",
                payload: null,
                TestTimeout,
                CancellationToken.None));
        var ping = control.CallAsync(
            "bus.ping",
            payload: null,
            TestTimeout,
            cancellation.Token);

        await WaitForSentCountAsync(node, 2);
        cancellation.Cancel();
        Assert.Multiple(() =>
        {
            Assert.That(rejected!.Code, Is.EqualTo(ErrorCode.TooManyRequests));
            Assert.That(node.Sent.Count, Is.EqualTo(2));
        });
        Assert.ThrowsAsync<RpcCallException>(async () => await occupied);
        Assert.ThrowsAsync<RpcCallException>(async () => await ping);
    }

    [Test]
    public async Task FailPending_RepeatedCalls_ShouldBeIdempotent()
    {
        var bus = new MessageBus();
        var node = new InMemoryTransport();
        bus.RegisterEndpoint(Node("session-1"), node);
        await using var client = Client(bus, "session-1", EndpointIds.Host);

        var call = client.CallAsync(
            "plugin.call.search",
            payload: null,
            TestTimeout,
            CancellationToken.None);
        await WaitForSentAsync(node);
        var error = BusError.For(ErrorCode.TransportDisconnected, "session stopped");

        client.FailPending(error);
        client.FailPending(error);
        var exception = Assert.ThrowsAsync<RpcCallException>(async () => await call);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(ErrorCode.TransportDisconnected));
            Assert.That(client.PendingCount, Is.Zero);
            Assert.That(bus.ResponseRouteCount, Is.Zero);
        });
    }

    private static EndpointRpcClient Client(
        MessageBus bus,
        string sessionId,
        string label,
        int pendingLimit = EndpointRpcClient.DefaultPendingLimit)
        => new(
            bus,
            new EndpointId("settings", sessionId, label, IsNode: false),
            pendingLimit: pendingLimit);

    private static EndpointId Node(string sessionId)
        => new("settings", sessionId, EndpointIds.NodeMain, IsNode: true);

    private static Envelope Response(EndpointId node, Envelope request, JsonNode? payload)
        => new()
        {
            Version = ProtocolVersion.Current,
            Id = $"response-{request.Id}",
            CorrelationId = request.Id,
            TraceId = request.TraceId,
            SessionId = node.SessionId,
            PluginId = node.PluginId,
            EndpointId = node.EndpointLabel,
            Kind = MessageKind.Response,
            Route = request.Route,
            Payload = payload,
        };

    private static async Task<Envelope> WaitForSentAsync(InMemoryTransport transport)
    {
        await WaitForSentCountAsync(transport, 1);
        return transport.Sent.ToArray()[^1];
    }

    private static async Task WaitForSentCountAsync(InMemoryTransport transport, int count)
    {
        var deadline = Environment.TickCount64 + (long)TestTimeout.TotalMilliseconds;
        while (transport.Sent.Count < count && Environment.TickCount64 < deadline)
        {
            await Task.Delay(5);
        }
        Assert.That(transport.Sent.Count, Is.GreaterThanOrEqualTo(count));
    }
}
