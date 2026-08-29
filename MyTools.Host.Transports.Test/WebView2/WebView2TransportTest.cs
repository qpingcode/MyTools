using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MyTools.Host.Core.Bus;
using MyTools.Host.Transports.WebView2;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Transports.Test.WebView2;

[TestFixture]
public class WebView2TransportTest
{
    private static EndpointBinding Binding =>
        new(PluginId: "settings", SessionId: "s1", EndpointId: "web-1");

    private sealed class FakeChannel : IWebViewMessageChannel
    {
        public readonly List<string> Posted = new();
        public event Action<string>? WebMessageReceived;
        public void PostWebMessageAsJson(string json) => Posted.Add(json);
        public void Emit(string json) => WebMessageReceived?.Invoke(json);
    }

    private static void CompleteHandshake(FakeChannel channel)
    {
        channel.Emit("""
            {"version":"3.0","id":"hs1","traceId":"hs1","sessionId":"","pluginId":"",
             "endpointId":"web-1","kind":"request","route":"bus.handshake","timeoutMs":5000,
             "payload":{"version":"3.0","supportedVersions":["3.0"]}}
            """);
    }

    [Test]
    public async Task Handshake_ShouldBindIdentityAndMarkReady()
    {
        var channel = new FakeChannel();
        var transport = new WebView2Transport(Binding, channel);

        CompleteHandshake(channel);

        Assert.That(await WaitForAsync(() => channel.Posted.Count > 0), Is.True);
        Assert.That(transport.IsHandshaken, Is.True);
        Assert.That(transport.NegotiatedVersion, Is.EqualTo(ProtocolVersion.Current));
        Assert.That(channel.Posted[0], Does.Contain("negotiatedVersion").Or.Contain("pluginId"));
    }

    [Test]
    public async Task Handshake_MajorMismatch_ShouldFailAndKeepClosed()
    {
        var channel = new FakeChannel();
        var transport = new WebView2Transport(Binding, channel);
        BusError? failed = null;
        transport.HandshakeFailed += err => failed = err;

        channel.Emit("""
            {"version":"9.0","id":"hs1","traceId":"hs1","sessionId":"","pluginId":"",
             "endpointId":"web-1","kind":"request","route":"bus.handshake","timeoutMs":5000,
             "payload":{"version":"9.0","supportedVersions":["9.0"]}}
            """);

        Assert.That(await WaitForAsync(() => channel.Posted.Count > 0), Is.True);
        Assert.That(transport.IsHandshaken, Is.False);
        Assert.That(failed, Is.Not.Null);
        Assert.That(failed!.Code, Is.EqualTo(ErrorCode.ProtocolMismatch));
        Assert.That(channel.Posted[0], Does.Contain("ProtocolMismatch"));
    }

    [Test]
    public async Task PluginCallBeforeHandshake_ShouldReplyPluginUnavailable()
    {
        var channel = new FakeChannel();
        var transport = new WebView2Transport(Binding, channel);

        channel.Emit("""
            {"version":"3.0","id":"c1","traceId":"c1","sessionId":"","pluginId":"",
             "endpointId":"web-1","kind":"request","route":"plugin.call.refresh","timeoutMs":1000,
             "payload":{}}
            """);

        Assert.That(await WaitForAsync(() => channel.Posted.Count > 0), Is.True);
        Assert.That(channel.Posted[0], Does.Contain("PluginUnavailable"));
        Assert.That(transport.IsHandshaken, Is.False);
    }

    [Test]
    public async Task HostCallFromPage_ShouldReplyCapabilityDenied()
    {
        var channel = new FakeChannel();
        var transport = new WebView2Transport(Binding, channel);
        CompleteHandshake(channel);
        Assert.That(await WaitForAsync(() => transport.IsHandshaken), Is.True);
        channel.Posted.Clear();

        channel.Emit("""
            {"version":"3.0","id":"h1","traceId":"h1","sessionId":"x","pluginId":"evil",
             "endpointId":"x","kind":"request","route":"host.call.getConfiguration","timeoutMs":1000,
             "payload":{}}
            """);

        Assert.That(await WaitForAsync(() => channel.Posted.Count > 0), Is.True);
        Assert.That(channel.Posted[0], Does.Contain("CapabilityDenied"));
    }

    [Test]
    public async Task RoundTrip_ViaMessageBus_ShouldDeliverEnvelopeResponse()
    {
        var bus = new MessageBus();
        var channel = new FakeChannel();
        var web = new WebView2Transport(Binding, channel);
        CompleteHandshake(channel);
        Assert.That(await WaitForAsync(() => web.IsHandshaken), Is.True);
        channel.Posted.Clear();

        var node = new MyTools.Host.Core.Transports.InMemoryTransport();
        bus.RegisterEndpoint(new EndpointId("settings", "s1", "web-1", IsNode: false), web);
        bus.RegisterEndpoint(new EndpointId("settings", "s1", "node-main", IsNode: true), node);

        channel.Emit("""
            {"version":"3.0","id":"req-9","traceId":"req-9","sessionId":"","pluginId":"",
             "endpointId":"web-1","kind":"request","route":"plugin.call.getConfiguration","timeoutMs":1000,
             "payload":{}}
            """);
        Assert.That(await WaitForAsync(() => node.Sent.Count > 0), Is.True);

        var req = node.Sent.ToArray()[0];
        Assert.That(req.Route, Is.EqualTo("plugin.call.getConfiguration"));
        node.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = "nresp",
            CorrelationId = req.Id,
            TraceId = req.TraceId,
            SessionId = "s1",
            PluginId = "settings",
            EndpointId = "node-main",
            Kind = MessageKind.Response,
            Route = req.Route,
            Payload = JsonNode.Parse("""{"categories":[]}"""),
        });

        Assert.That(await WaitForAsync(() => channel.Posted.Count > 0), Is.True);
        Assert.That(channel.Posted[0], Does.Contain("categories"));
        Assert.That(channel.Posted[0], Does.Contain("\"kind\":\"response\""));
    }

    [Test]
    public async Task PluginCall_ShouldEnrichPayloadWithHostContext()
    {
        Envelope? received = null;
        var channel = new FakeChannel();
        var transport = new WebView2Transport(
            Binding,
            channel,
            enrichPluginCallPayload: payload =>
            {
                payload["itemId"] = "item-1";
                payload["query"] = "hello";
                return payload;
            });
        CompleteHandshake(channel);
        Assert.That(await WaitForAsync(() => transport.IsHandshaken), Is.True);
        transport.MessageReceived += env => received = env;

        channel.Emit("""
            {"version":"3.0","id":"r1","traceId":"r1","sessionId":"","pluginId":"",
             "endpointId":"web-1","kind":"request","route":"plugin.call.refresh","timeoutMs":1000,
             "payload":{"currentQuery":"x"}}
            """);

        Assert.That(await WaitForAsync(() => received is not null), Is.True);
        Assert.That(received!.Route, Is.EqualTo("plugin.call.refresh"));
        Assert.That(received.Payload!["itemId"]!.GetValue<string>(), Is.EqualTo("item-1"));
        Assert.That(received.Payload!["query"]!.GetValue<string>(), Is.EqualTo("hello"));
        Assert.That(received.Payload!["currentQuery"]!.GetValue<string>(), Is.EqualTo("x"));
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(10);
        }
        return predicate();
    }
}
