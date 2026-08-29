using NUnit.Framework;
using MyTools.Common.Plugins;

namespace MyTools.Common.Test.Plugins;

[TestFixture]
public class PluginIdTest
{
    [Test]
    public void Constructor_TrimsAndRejectsBlank()
    {
        Assert.That(new PluginId("  quick-text  ").Value, Is.EqualTo("quick-text"));
        Assert.That(() => new PluginId("  "), Throws.ArgumentException);
        Assert.That(() => new PluginId(""), Throws.ArgumentException);
    }

    [Test]
    public void Equality_IsCaseInsensitive()
    {
        Assert.That(new PluginId("ClipBoard"), Is.EqualTo(new PluginId("clipboard")));
        Assert.That(new PluginId("ClipBoard").GetHashCode(), Is.EqualTo(new PluginId("clipboard").GetHashCode()));
        Assert.That(new PluginId("a"), Is.Not.EqualTo(new PluginId("b")));
    }
}
