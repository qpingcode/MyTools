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
        new(PluginId: "settings", EntryId: "main", SessionId: "s1", EndpointId: "web-1");

    private sealed class FakeChannel : IWebViewMessageChannel
    {
        public readonly List<string> Posted = new();
        public event Action<string>? WebMessageReceived;
        public void PostWebMessageAsJson(string json) => Posted.Add(json);
        public void Emit(string json) => WebMessageReceived?.Invoke(json);
    }

    [Test]
    public void LegacyToolCall_ShouldRaiseNormalizedDetailCallEnvelope()
    {
        var channel = new FakeChannel();
        Envelope? received = null;
        var transport = new WebView2Transport(Binding, channel);
        transport.MarkHandshaken();
        transport.MessageReceived += env => received = env;

        channel.Emit("""{"type":"tool-call","requestId":"r1","action":"getConfiguration","payload":{"x":1}}""");

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Route, Is.EqualTo("plugin.call.detailCall"));
        Assert.That(received.Id, Is.EqualTo("r1"));
        Assert.That(received.PluginId, Is.EqualTo("settings"));
        Assert.That(received.SessionId, Is.EqualTo("s1"));
        Assert.That(received.Payload!["action"]!.GetValue<string>(), Is.EqualTo("getConfiguration"));
    }

    [Test]
    public async Task HostCallFromPage_ShouldReplyCapabilityDenied()
    {
        var channel = new FakeChannel();
        var transport = new WebView2Transport(Binding, channel);
        transport.MarkHandshaken();

        channel.Emit("""
            {"version":"3.0","id":"h1","traceId":"h1","sessionId":"x","pluginId":"evil","entryId":"x",
             "endpointId":"x","kind":"request","route":"host.call.getConfiguration","timeoutMs":1000,
             "payload":{}}
            """);

        Assert.That(await WaitForAsync(() => channel.Posted.Count > 0), Is.True);
        Assert.That(channel.Posted[0], Does.Contain("CapabilityDenied"));
    }

    [Test]
    public async Task SendAsync_LegacyCorrelation_ShouldRewriteToToolResponse()
    {
        var channel = new FakeChannel();
        var transport = new WebView2Transport(Binding, channel);
        transport.MarkHandshaken();
        transport.MessageReceived += _ => { };
        channel.Emit("""{"type":"tool-call","requestId":"r2","action":"save","payload":{}}""");

        await transport.SendAsync(new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = "resp",
            CorrelationId = "r2",
            TraceId = "r2",
            SessionId = "s1",
            PluginId = "settings",
            EntryId = "main",
            EndpointId = "web-1",
            Kind = MessageKind.Response,
            Route = "plugin.call.detailCall",
            Payload = JsonNode.Parse("""{"result":{"ok":true}}"""),
        }, default);

        Assert.That(channel.Posted, Has.Count.EqualTo(1));
        Assert.That(channel.Posted[0], Does.Contain("\"type\":\"tool-response\""));
        Assert.That(channel.Posted[0], Does.Contain("\"ok\":true"));
    }

    [Test]
    public async Task Handshake_ShouldBindIdentityAndMarkReady()
    {
        var channel = new FakeChannel();
        var transport = new WebView2Transport(Binding, channel);

        channel.Emit("""
            {"version":"3.0","id":"hs1","traceId":"hs1","sessionId":"","pluginId":"","entryId":"",
             "endpointId":"web-1","kind":"request","route":"bus.handshake","timeoutMs":5000,
             "payload":{"version":"3.0","supportedVersions":["3.0"]}}
            """);

        Assert.That(await WaitForAsync(() => channel.Posted.Count > 0), Is.True);
        Assert.That(transport.IsHandshaken, Is.True);
        Assert.That(channel.Posted[0], Does.Contain("negotiatedVersion").Or.Contain("pluginId"));
    }

    [Test]
    public async Task RoundTrip_ViaMessageBus_ShouldDeliverResponseAsToolResponse()
    {
        var bus = new MessageBus();
        var channel = new FakeChannel();
        var web = new WebView2Transport(Binding, channel);
        web.MarkHandshaken();
        var node = new MyTools.Host.Core.Transports.InMemoryTransport();

        bus.RegisterEndpoint(new EndpointId("settings", "main", "s1", "web-1", IsNode: false), web);
        bus.RegisterEndpoint(new EndpointId("settings", "main", "s1", "node-main", IsNode: true), node);

        channel.Emit("""{"type":"tool-call","requestId":"req-9","action":"getConfiguration","payload":{}}""");
        Assert.That(await WaitForAsync(() => node.Sent.Count > 0), Is.True);

        var req = node.Sent.ToArray()[0];
        node.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = "nresp",
            CorrelationId = req.Id,
            TraceId = req.TraceId,
            SessionId = "s1",
            PluginId = "settings",
            EntryId = "main",
            EndpointId = "node-main",
            Kind = MessageKind.Response,
            Route = req.Route,
            Payload = JsonNode.Parse("""{"result":{"categories":[]}}"""),
        });

        Assert.That(await WaitForAsync(() => channel.Posted.Count > 0), Is.True);
        Assert.That(channel.Posted[0], Does.Contain("tool-response"));
        Assert.That(channel.Posted[0], Does.Contain("categories"));
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
