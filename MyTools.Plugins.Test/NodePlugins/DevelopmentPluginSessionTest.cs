using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
[NonParallelizable]
public sealed class DevelopmentPluginSessionTest
{
    [SetUp]
    public void SetUp() => DevelopmentPluginSession.Clear();

    [TearDown]
    public void TearDown() => DevelopmentPluginSession.Clear();

    [Test]
    public void DeactivateRemovesOnlyTheRequestedDevelopmentPlugin()
    {
        DevelopmentPluginSession.Activate("first-plugin");
        DevelopmentPluginSession.Activate("second-plugin");

        DevelopmentPluginSession.Deactivate("first-plugin");

        Assert.Multiple(() =>
        {
            Assert.That(DevelopmentPluginSession.IsActive("first-plugin"), Is.False);
            Assert.That(DevelopmentPluginSession.IsActive("second-plugin"), Is.True);
        });
    }
}
