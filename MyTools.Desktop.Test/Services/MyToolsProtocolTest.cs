using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public sealed class MyToolsProtocolTest
{
    [Test]
    public void TryParse_ReadsInstallQuery()
    {
        Assert.That(MyToolsProtocol.TryParse("mytools://install?pluginId=chat", out var request), Is.True);
        Assert.That(request.Action, Is.EqualTo("install"));
        Assert.That(request.PluginId, Is.EqualTo("chat"));
        Assert.That(request.Version, Is.Null);
    }

    [Test]
    public void TryParse_ReadsVersionAndQuotedCommandLineValue()
    {
        Assert.That(MyToolsProtocol.TryParse("\"mytools://install?pluginId=chat&version=1.2.3\"", out var request), Is.True);
        Assert.That(request.PluginId, Is.EqualTo("chat"));
        Assert.That(request.Version, Is.EqualTo("1.2.3"));
    }

    [Test]
    public void TryParse_ReadsPathForm()
    {
        Assert.That(MyToolsProtocol.TryParse("mytools://install/formatter", out var request), Is.True);
        Assert.That(request.Action, Is.EqualTo("install"));
        Assert.That(request.PluginId, Is.EqualTo("formatter"));
    }

    [Test]
    public void TryParse_RejectsOtherSchemes()
    {
        Assert.That(MyToolsProtocol.TryParse("https://example.com/install?pluginId=chat", out _), Is.False);
        Assert.That(MyToolsProtocol.IsActivation("not-a-uri"), Is.False);
    }

    [Test]
    public void InstallUri_RoundTrips()
    {
        var uri = MyToolsProtocol.InstallUri("store", "2.0.0");
        Assert.That(MyToolsProtocol.TryParse(uri, out var request), Is.True);
        Assert.That(request.PluginId, Is.EqualTo("store"));
        Assert.That(request.Version, Is.EqualTo("2.0.0"));
    }
}
