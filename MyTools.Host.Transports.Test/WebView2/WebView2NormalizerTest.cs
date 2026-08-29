using System.Text.Json.Nodes;
using MyTools.Host.Transports.WebView2;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Framing;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Transports.Test.WebView2;

[TestFixture]
public class WebView2NormalizerTest
{
    private static readonly EndpointBinding Binding =
        new(PluginId: "settings", SessionId: "s1", EndpointId: "web-1");

    private static JsonNode Payload => JsonNode.Parse("""{"x":1}""")!;

    // Test 28 — identity stamping: outbound session/plugin/endpoint overwritten by the binding.
    [Test]
    public void Normalize_Outbound_ShouldStampBoundIdentityOverPageDeclaredFields()
    {
        var normalizer = new WebView2Normalizer(Binding);
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "m1", TraceId = "t1",
            SessionId = "DECLARED-BY-PAGE",     // page tries to set its own session
            PluginId = "evil",                   // page tries to impersonate another plugin
            EndpointId = "forged",
            Kind = MessageKind.Request, Route = "plugin.call.save", TimeoutMs = 1000,
            Payload = Payload
        };

        var result = normalizer.NormalizeOutbound(env);

        Assert.That(result.Envelope!.SessionId, Is.EqualTo("s1"));
        Assert.That(result.Envelope!.PluginId, Is.EqualTo("settings"));
        Assert.That(result.Envelope!.EndpointId, Is.EqualTo("web-1"));
    }

    // Test 28b — identity cannot be switched after binding.
    [Test]
    public void Normalize_Outbound_ShouldAlwaysUseBindingRegardlessOfInput()
    {
        var normalizer = new WebView2Normalizer(Binding);
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "m", TraceId = "t",
            SessionId = "x", PluginId = "y", EndpointId = "w",
            Kind = MessageKind.Request, Route = "plugin.call.save", TimeoutMs = 1000,
            Payload = Payload
        };

        var r1 = normalizer.NormalizeOutbound(env);
        var r2 = normalizer.NormalizeOutbound(env);

        Assert.Multiple(() =>
        {
            Assert.That(r1.Envelope!.PluginId, Is.EqualTo(r2.Envelope!.PluginId).And.EqualTo("settings"));
            Assert.That(r1.Envelope!.EndpointId, Is.EqualTo(r2.Envelope!.EndpointId).And.EqualTo("web-1"));
        });
    }

    // Test 32 — webview calling host.call.* must be denied.
    [Test]
    public void Normalize_Outbound_HostCallRoute_ShouldReturnCapabilityDenied()
    {
        var normalizer = new WebView2Normalizer(Binding);
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "m", TraceId = "t", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = MessageKind.Request, Route = "host.call.configuration.write", TimeoutMs = 1000,
            Payload = Payload
        };

        var result = normalizer.NormalizeOutbound(env);

        Assert.That(result.IsRejected, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.CapabilityDenied));
    }

    // Test 28c — only plugin.call.* allowed from webview; plugin.event.* also allowed.
    [TestCase("plugin.call.save")]
    [TestCase("plugin.event.changed")]
    public void Normalize_Outbound_AllowedRoutes_ShouldNotReject(string route)
    {
        var normalizer = new WebView2Normalizer(Binding);
        var kind = route.StartsWith("plugin.event.") ? MessageKind.Event : MessageKind.Request;
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "m", TraceId = "t", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = kind, Route = route, TimeoutMs = 1000, Payload = Payload
        };

        var result = normalizer.NormalizeOutbound(env);

        Assert.That(result.IsRejected, Is.False);
    }

    // Test 31 — outbound message byte-size precheck (over global MaxFrameBytes rejected).
    [Test]
    public void Normalize_Outbound_OverGlobalMaxFrameBytes_ShouldReturnMessageTooLarge()
    {
        var normalizer = new WebView2Normalizer(Binding);
        var big = new string('x', FrameLimits.MaxFrameBytes + 10);
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "m", TraceId = "t", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = MessageKind.Request, Route = "plugin.call.save", TimeoutMs = 1000,
            Payload = JsonNode.Parse($"{{\"big\":\"{big}\"}}")
        };

        var result = normalizer.NormalizeOutbound(env, maxBytes: FrameLimits.MaxFrameBytes);

        Assert.That(result.IsRejected, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.MessageTooLarge));
    }

    // Test 31b — under route payload cap but over global → MessageTooLarge.
    [Test]
    public void Normalize_Outbound_OverRoutePayloadCapButUnderGlobal_ShouldReturnMessageTooLarge()
    {
        var normalizer = new WebView2Normalizer(Binding);
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "m", TraceId = "t", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = MessageKind.Request, Route = "plugin.call.save", TimeoutMs = 1000,
            Payload = JsonNode.Parse("{\"x\":\"" + new string('y', 500) + "\"}")
        };

        var result = normalizer.NormalizeOutbound(env, maxBytes: 100); // tiny route cap

        Assert.That(result.IsRejected, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.MessageTooLarge));
    }

    // Test 29 — on Node restart, the endpoint binding is invalidated (old page's messages rejected).
    [Test]
    public void InvalidateOldBinding_AfterReload_OldEndpointMessagesRejected()
    {
        var normalizer = new WebView2Normalizer(Binding);
        var env = new Envelope
        {
            Version = ProtocolVersion.Current, Id = "m", TraceId = "t", SessionId = "s1",
            PluginId = "settings", EndpointId = "web-1",
            Kind = MessageKind.Request, Route = "plugin.call.save", TimeoutMs = 1000,
            Payload = Payload
        };

        // Before reload: allowed.
        Assert.That(normalizer.NormalizeOutbound(env).IsRejected, Is.False);

        // Node restarts → old page invalidated.
        normalizer.Invalidate();

        var result = normalizer.NormalizeOutbound(env);
        Assert.That(result.IsRejected, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.PluginUnavailable));
    }

    // Test 30 — baseline CSP policy is non-empty and contains script-src/default-src.
    [Test]
    public void BaselineCsp_ShouldContainScriptAndDefaultSrc()
    {
        var csp = WebView2Normalizer.BaselineCsp;

        Assert.That(csp, Does.Contain("script-src"));
        Assert.That(csp, Does.Contain("default-src"));
    }
}
