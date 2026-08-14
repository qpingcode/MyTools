using MyTools.Protocol.Errors;
using MyTools.Protocol.Routing;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Routing;

[TestFixture]
public class RouteRulesTest
{
    [TestCase("plugin.call.saveConfiguration", ExpectedResult = true)]
    [TestCase("host.call.configuration.write", ExpectedResult = true)]
    [TestCase("plugin.event.dataChanged", ExpectedResult = true)]
    [TestCase("host.event.themeChanged", ExpectedResult = true)]
    [TestCase("bus.handshake", ExpectedResult = true)]
    [TestCase("bus.ping", ExpectedResult = true)]
    public bool Classify_KnownRoute_ShouldBeLegal(string route)
        => RouteRules.Classify(route).IsLegal;

    [TestCase("bus.cancel", TestName = "reserved bus.cancel")]
    [TestCase("bus.subscribe", TestName = "reserved bus.subscribe")]
    [TestCase("bus.unsubscribe", TestName = "reserved bus.unsubscribe")]
    [TestCase("diagnostics.inspect", TestName = "reserved diagnostics.*")]
    public void Classify_ReservedRoute_ShouldReturnRouteNotFound(string route)
    {
        var c = RouteRules.Classify(route);

        Assert.That(c.IsLegal, Is.False);
        Assert.That(c.Error!.Code, Is.EqualTo(ErrorCode.RouteNotFound));
    }

    [TestCase("plugin.call")]
    [TestCase("unknown.route")]
    [TestCase("plugin.fetch.something")]
    [TestCase("")]
    public void Classify_UnknownRoute_ShouldReturnRouteNotFound(string route)
    {
        var c = RouteRules.Classify(route);

        Assert.That(c.IsLegal, Is.False);
        Assert.That(c.Error!.Code, Is.EqualTo(ErrorCode.RouteNotFound));
    }

    [TestCase("plugin.call.saveConfiguration", RouteNamespace.PluginCall)]
    [TestCase("host.call.configuration.read", RouteNamespace.HostCall)]
    [TestCase("plugin.event.changed", RouteNamespace.PluginEvent)]
    [TestCase("host.event.theme", RouteNamespace.HostEvent)]
    [TestCase("bus.handshake", RouteNamespace.Bus)]
    [TestCase("bus.ping", RouteNamespace.Bus)]
    public void Classify_ShouldReturnCorrectNamespace(string route, RouteNamespace expected)
    {
        var c = RouteRules.Classify(route);

        Assert.That(c.Namespace, Is.EqualTo(expected));
    }
}
