using MyTools.Host.Core.Capabilities;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Capabilities;

[TestFixture]
public class HostCallCapabilityMapTest
{
    [TestCase("host.call.getConfiguration", "configuration.write")]
    [TestCase("host.call.saveConfiguration", "configuration.write")]
    [TestCase("host.call.configuration.write", "configuration.write")]
    [TestCase("host.call.clipboard.read", "clipboard.read")]
    [TestCase("host.call.suspendHotkeys", "configuration.write")]
    [TestCase("host.call.checkHotKey", "configuration.write")]
    [TestCase("host.call.captureInputAction", "configuration.write")]
    public void Resolve_ShouldMapLegacyAndPassThroughCapabilityShapedRoutes(string route, string expected)
    {
        Assert.That(HostCallCapabilityMap.Resolve(route), Is.EqualTo(expected));
    }
}
