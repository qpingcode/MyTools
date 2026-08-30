using Moq;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Plugins;
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
        storage.Setup(value => value.Exists(It.IsAny<string>(), It.IsAny<PluginId?>()))
            .Returns((string key, PluginId? _) => stored.ContainsKey(key));
        storage.Setup(value => value.Retrieve(It.IsAny<string>(), It.IsAny<PluginId?>()))
            .Returns((string key, PluginId? _) => stored.GetValueOrDefault(key));
        storage.Setup(value => value.Store(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PluginId?>()))
            .Callback((string key, string value, PluginId? _) => stored[key] = value);

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
    public void RemoveCategory_RemovesFlatCategoryAndSettingsFromIndexes()
    {
        var registry = new ConfigurationRegistry(new Mock<IConfigurationStorage>().Object);
        var category = registry.AddCategory("development-plugin", "Development plugin", "");
        registry.AddSetting(category, "Endpoint", "Endpoint", "", "https://example.test", serializer: null);

        var removed = registry.RemoveCategory("development-plugin");

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(registry.FindCategory("development-plugin"), Is.Null);
            Assert.That(registry.FindSetting("development-plugin.Endpoint"), Is.Null);
            Assert.That(registry.GetRootCategories(), Is.Empty);
        });
    }

    [Test]
    public void AddSetting_UsesGlobalKeyAndPluginRelativeStorageKey()
    {
        var registry = new ConfigurationRegistry(new Mock<IConfigurationStorage>().Object);
        var pluginId = new PluginId("quick-text");
        var category = registry.AddCategory("quick-text", "Quick Text", "", pluginId: pluginId);
        var rootSetting = registry.AddSetting(category, "Phrases", "Phrases", "", "[]", serializer: null);

        Assert.Multiple(() =>
        {
            Assert.That(rootSetting.PluginId, Is.EqualTo(pluginId));
            Assert.That(rootSetting.Key, Is.EqualTo("quick-text.Phrases"));
            Assert.That(rootSetting.StorageKey, Is.EqualTo("Phrases"));
        });
    }

    [Test]
    public void AddSetting_HostSettingUsesGlobalKeyForStorage()
    {
        var registry = new ConfigurationRegistry(new Mock<IConfigurationStorage>().Object);
        var category = registry.AddCategory("General", "常规", "");

        var setting = registry.AddSetting(category, "Language", "语言", "", "zh-CN", serializer: null);

        Assert.Multiple(() =>
        {
            Assert.That(setting.Key, Is.EqualTo(GeneralSettings.LanguagePath));
            Assert.That(setting.StorageKey, Is.EqualTo(GeneralSettings.LanguagePath));
            Assert.That(registry.FindSetting(GeneralSettings.LanguagePath), Is.SameAs(setting));
        });
    }
}
