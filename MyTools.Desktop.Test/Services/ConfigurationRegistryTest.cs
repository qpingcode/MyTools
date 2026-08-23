using Moq;
using MyTools.Common.Config.Interfaces;
using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class ConfigurationRegistryTest
{
    [Test]
    public void RemoveCategory_RemovesCategoryChildrenAndSettingsFromIndexes()
    {
        var registry = new ConfigurationRegistry(new Mock<IConfigurationStorage>().Object);
        var category = registry.AddCategory("development-plugin", "Development plugin", "");
        var child = registry.AddCategory("advanced", "Advanced", "", category);
        registry.AddSetting(category, "Endpoint", "Endpoint", "", "https://example.test", serializer: null);
        registry.AddSetting(child, "Retries", "Retries", "", 3, serializer: null);

        var removed = registry.RemoveCategory("development-plugin");

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(registry.FindCategory("development-plugin"), Is.Null);
            Assert.That(registry.FindCategory("development-plugin.advanced"), Is.Null);
            Assert.That(registry.FindSetting("development-plugin.Endpoint"), Is.Null);
            Assert.That(registry.FindSetting("development-plugin.advanced.Retries"), Is.Null);
            Assert.That(registry.GetRootCategories(), Is.Empty);
        });
    }
}
