using MyTools.Common.Config;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

public sealed class DevelopmentPluginRegistrationStoreTest
{
    [Test]
    public void Paths_ShouldBelongToCreatePluginDataDirectory()
    {
        var expected = ConfigPath.PluginDataDirectory("create-plugin");

        Assert.That(DevelopmentPluginRegistrationStore.DataDirectory, Is.EqualTo(expected));
        Assert.That(
            DevelopmentPluginRegistrationStore.FilePath,
            Is.EqualTo(Path.Combine(expected, "development-plugins.json")));
    }
}
