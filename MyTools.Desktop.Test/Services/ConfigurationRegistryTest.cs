using Moq;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class ConfigurationRegistryTest
{
    [Test]
    public void SaveChanges_RaisesConfigurationChangedAfterPersistingChangedValue()
    {
        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var storage = new Mock<IConfigurationStorage>();
        storage.Setup(value => value.Exists(It.IsAny<string>()))
            .Returns((string key) => stored.ContainsKey(key));
        storage.Setup(value => value.Retrieve(It.IsAny<string>()))
            .Returns((string key) => stored.GetValueOrDefault(key));
        storage.Setup(value => value.Store(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string key, string value) => stored[key] = value);

        var registry = new ConfigurationRegistry(storage.Object);
        var category = registry.AddCategory("Test", "Test", "");
        var setting = registry.AddSetting(category, "Value", "Value", "", "old", serializer: null);
        ConfigurationChangedEventArgs? received = null;
        registry.ConfigurationChanged += (_, args) => received = args;

        setting.CurrentValue = "new";
        registry.SaveChanges();

        Assert.Multiple(() =>
        {
            Assert.That(stored["Test.Value"], Is.EqualTo("new"));
            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Setting, Is.SameAs(setting));
            Assert.That(received.OldValue, Is.EqualTo("old"));
            Assert.That(received.NewValue, Is.EqualTo("new"));
        });
    }

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
