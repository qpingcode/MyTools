using MyTools.Protocol.Routing;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Routing;

[TestFixture]
public class RoutesTest
{
    [Test]
    public void WellKnownPluginCallRoutes_ShouldUsePluginCallPrefix()
    {
        Assert.That(Routes.PluginCall.Initialize, Is.EqualTo("plugin.call.initialize"));
        Assert.That(Routes.PluginCall.Search, Is.EqualTo("plugin.call.search"));
        Assert.That(Routes.PluginCall.InvokeAction, Is.EqualTo("plugin.call.invokeAction"));
    }

    [Test]
    public void WellKnownHostEventRoutes_ShouldUseHostEventPrefix()
    {
        Assert.That(Routes.HostEvent.Initialize, Is.EqualTo("host.event.initialize"));
        Assert.That(Routes.HostEvent.Search, Is.EqualTo("host.event.search"));
        Assert.That(Routes.HostEvent.Key, Is.EqualTo("host.event.key"));
        Assert.That(Routes.HostEvent.LanguageChanged, Is.EqualTo("host.event.languageChanged"));
        Assert.That(Routes.HostEvent.ThemeChanged, Is.EqualTo("host.event.themeChanged"));
        Assert.That(Routes.HostEvent.Of("tick"), Is.EqualTo("host.event.tick"));
        Assert.That(Routes.HostEvent.Of(Routes.HostEvent.Search), Is.EqualTo(Routes.HostEvent.Search));
    }

    [Test]
    public void PluginCallOf_ShouldPrefixBareMethodAndLeaveFullRoute()
    {
        Assert.That(Routes.PluginCall.Of("echo"), Is.EqualTo("plugin.call.echo"));
        Assert.That(Routes.PluginCall.Of(Routes.PluginCall.Search), Is.EqualTo(Routes.PluginCall.Search));
    }

    [Test]
    public void HostCallOf_AndStrip_ShouldRoundTrip()
    {
        var route = Routes.HostCall.Of("getConfiguration");
        Assert.That(route, Is.EqualTo("host.call.getConfiguration"));
        Assert.That(Routes.StripHostCall(route), Is.EqualTo("getConfiguration"));
        Assert.That(Routes.StripHostCall("alreadyBare"), Is.EqualTo("alreadyBare"));
    }

    [Test]
    public void PluginEventOf_ShouldNotDoublePrefix()
    {
        Assert.That(Routes.PluginEvent.Of("tick"), Is.EqualTo("plugin.event.tick"));
        Assert.That(Routes.PluginEvent.Of("plugin.event.tick"), Is.EqualTo("plugin.event.tick"));
    }

    [Test]
    public void IsHelpers_ShouldMatchExactAndPrefixedRoutes()
    {
        Assert.That(Routes.IsPing(Routes.Bus.Ping), Is.True);
        Assert.That(Routes.IsHandshake(Routes.Bus.Handshake), Is.True);
        Assert.That(Routes.IsHostCall("host.call.x"), Is.True);
        Assert.That(Routes.IsPluginCall("plugin.call.x"), Is.True);
        Assert.That(Routes.IsDiagnostics("diagnostics.inspect"), Is.True);
        Assert.That(Routes.HasSegmentAfterPrefix("plugin.call", Routes.Prefix.PluginCall), Is.False);
        Assert.That(Routes.HasSegmentAfterPrefix("plugin.call.save", Routes.Prefix.PluginCall), Is.True);
    }
}
