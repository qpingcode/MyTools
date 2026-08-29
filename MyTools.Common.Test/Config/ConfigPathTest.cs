using MyTools.Common.Config;
using NUnit.Framework;

namespace MyTools.Common.Test.Config;

[TestFixture]
public class ConfigPathTest
{
    [Test]
    public void PluginSettingsPath_IsUnderPluginsData()
    {
        var path = ConfigPath.PluginSettingsPath("quick-text");

        Assert.Multiple(() =>
        {
            Assert.That(path, Does.StartWith(ConfigPath.PluginsDataPath));
            Assert.That(path, Does.EndWith(Path.Combine("quick-text", ConfigPath.PluginSettingsFileName)));
        });
    }

    [Test]
    public void SanitizePluginId_ReplacesInvalidFileNameCharacters()
    {
        Assert.That(ConfigPath.SanitizePluginId("a:b"), Is.EqualTo("a_b"));
        Assert.That(ConfigPath.SanitizePluginId("  "), Is.EqualTo("_plugin"));
    }
}
