using System.Collections.Generic;
using System.Threading.Tasks;
using MyTools.Host.Core.Capabilities;
using MyTools.Protocol.Errors;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Capabilities;

[TestFixture]
public class CapabilityGatewayTest
{
    private static PluginManifest Manifest(params string[] caps)
        => new("settings", caps);

    [Test]
    public void Authorize_UndeclaredCapability_ShouldReturnCapabilityNotDeclared()
    {
        var gw = new CapabilityGateway();
        gw.RegisterManifest(Manifest("clipboard.read"));

        var result = gw.Authorize("settings", "clipboard.write");

        Assert.That(result.IsAllowed, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.CapabilityNotDeclared));
    }

    [Test]
    public void Authorize_DeclaredCapability_ShouldBeGranted()
    {
        var gw = new CapabilityGateway();
        gw.RegisterManifest(Manifest("clipboard.read", "configuration.write"));

        var result = gw.Authorize("settings", "configuration.write");

        Assert.That(result.IsAllowed, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void Authorize_ShouldRecordAuditEntry()
    {
        var gw = new CapabilityGateway();
        gw.RegisterManifest(Manifest("clipboard.read"));

        gw.Authorize("settings", "clipboard.read");

        var audit = gw.AuditEntries;
        Assert.That(audit, Has.Count.EqualTo(1));
        Assert.That(audit[0].Route, Is.EqualTo("clipboard.read"));
        Assert.That(audit[0].Allowed, Is.True);
        Assert.That(audit[0].PluginId, Is.EqualTo("settings"));
    }

    [Test]
    public void Authorize_Denied_ShouldRecordFailedAuditEntry()
    {
        var gw = new CapabilityGateway();
        gw.RegisterManifest(Manifest("clipboard.read"));

        gw.Authorize("settings", "clipboard.write");

        var audit = gw.AuditEntries;
        Assert.That(audit[0].Allowed, Is.False);
        Assert.That(audit[0].Route, Is.EqualTo("clipboard.write"));
    }

    [Test]
    public void Authorize_UnknownPlugin_ShouldReturnCapabilityNotDeclared()
    {
        var gw = new CapabilityGateway();

        var result = gw.Authorize("not-registered",  "clipboard.read");

        Assert.That(result.IsAllowed, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.CapabilityNotDeclared));
    }

    [Test]
    public void RegisterAndAuthorize_FromManyThreads_ShouldNotCorruptState()
    {
        var gw = new CapabilityGateway();
        const int n = 64;
        Parallel.For(0, n, i =>
        {
            var plugin = $"p{i}";
            gw.RegisterManifest(new PluginManifest(plugin,  ["configuration.write"]));
            var allowed = gw.Authorize(plugin,  "configuration.write");
            var denied = gw.Authorize(plugin, "clipboard.read");
            Assert.That(allowed.IsAllowed, Is.True);
            Assert.That(denied.IsAllowed, Is.False);
            gw.UnregisterManifest(plugin);
        });

        Assert.That(gw.AuditEntries, Has.Count.EqualTo(n * 2));
    }
}
