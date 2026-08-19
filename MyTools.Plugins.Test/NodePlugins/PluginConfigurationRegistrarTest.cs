using System.Text.Json;
using System.Text.Json.Nodes;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Localization;
using MyTools.Plugins.NodePlugins;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class PluginConfigurationRegistrarTest
{
    [Test]
    public void SettingName_ShouldStripPluginPrefixes()
    {
        Assert.That(PluginConfigurationRegistrar.SettingName("snippet", "Phrases"), Is.EqualTo("Phrases"));
        Assert.That(PluginConfigurationRegistrar.SettingName("snippet", "snippet.Phrases"), Is.EqualTo("Phrases"));
        Assert.That(PluginConfigurationRegistrar.SettingName("snippet", "Plugins.Snippet.Phrases"), Is.EqualTo("Phrases"));
    }

    [Test]
    public void NormalizeIcon_ShouldPrefixMdi()
    {
        Assert.That(PluginConfigurationRegistrar.NormalizeIcon(null), Is.Null);
        Assert.That(PluginConfigurationRegistrar.NormalizeIcon("  "), Is.Null);
        Assert.That(PluginConfigurationRegistrar.NormalizeIcon("mdi-message-text-outline"), Is.EqualTo("mdi-message-text-outline"));
        Assert.That(PluginConfigurationRegistrar.NormalizeIcon("message-text-outline"), Is.EqualTo("mdi-message-text-outline"));
    }

    [Test]
    public void Register_ShouldAddArraySettingOnce()
    {
        var registry = new FakeRegistry();
        var configuration = new List<PluginConfigurationSettingV3>
        {
            new()
            {
                Key = "Phrases",
                Type = "array",
                Label = new LocalizedNameV3 { Key = "Plugin.Snippet.Setting.Phrases", DefaultValue = "Phrases" },
                DefaultValue = JsonNode.Parse("[]"),
                Schema = new PluginConfigurationSchemaV3
                {
                    Properties =
                    [
                        new() { Key = "trigger", Type = "string", Label = new LocalizedNameV3 { Key = "t", DefaultValue = "Trigger" } },
                        new() { Key = "timestamp", Type = "hidden", DefaultValue = JsonValue.Create("${DateTime.Now}") },
                        new() { Key = "content", Type = "string", UiHint = "textarea", Table = false }
                    ]
                }
            }
        };

        PluginConfigurationRegistrar.Register(registry, "snippet", "Snippet", "desc", configuration, new FallbackLocalization(), "message-text-outline");
        PluginConfigurationRegistrar.Register(registry, "snippet", "Snippet", "desc", configuration, new FallbackLocalization(), "message-text-outline");

        Assert.That(registry.Categories, Has.Count.EqualTo(1));
        Assert.That(registry.Categories[0].Icon, Is.EqualTo("mdi-message-text-outline"));
        Assert.That(registry.Settings, Has.Count.EqualTo(1));
        var setting = registry.Settings[0];
        Assert.That(setting.FullPath, Is.EqualTo("snippet.Phrases"));
        Assert.That(setting.ValueType, Is.EqualTo(SettingValueTypes.Array));
        Assert.That(setting.UiHint, Is.EqualTo("table"));
        Assert.That(setting.Title, Is.EqualTo("Phrases"));
        Assert.That(setting.Schema, Is.Not.Null);
        Assert.That(setting.Schema!.Properties, Has.Count.EqualTo(3));
        Assert.That(setting.Schema.Properties[1].Hidden, Is.True);
        Assert.That(setting.Schema.Properties[1].DefaultValue, Is.EqualTo("${DateTime.Now}"));
        Assert.That(setting.Schema.Properties[2].UiHint, Is.EqualTo("textarea"));
        Assert.That(setting.Schema.Properties[2].Table, Is.False);
        Assert.That(setting.Schema.Properties[0].Table, Is.True);
        Assert.That(((JsonElement)setting.DefaultValue!).GetRawText(), Is.EqualTo("[]"));
    }

    private sealed class FallbackLocalization : ILocalizationService
    {
        public string CurrentLocale => "en-US";
        public event EventHandler<LocaleChangedEventArgs>? LocaleChanged
        {
            add { }
            remove { }
        }

        public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null)
            => defaultValue;
    }

    private sealed class FakeRegistry : IConfigurationRegistry
    {
        public List<ConfigurationCategory> Categories { get; } = [];
        public List<ConfigurationSetting> Settings { get; } = [];

        public ConfigurationCategory AddCategory(string name, string description, ConfigurationCategory? parent = null, bool IsSelectable = true)
            => AddCategory(name, name, description, parent, IsSelectable);

        public ConfigurationCategory AddCategory(string key, string name, string description, ConfigurationCategory? parent = null, bool IsSelectable = true)
        {
            var category = new ConfigurationCategory { Key = key, Name = name, Description = description, Parent = parent };
            Categories.Add(category);
            return category;
        }

        public IEnumerable<ConfigurationCategory> GetRootCategories() => Categories;

        public ConfigurationSetting AddSetting<T>(
            ConfigurationCategory category,
            string name,
            string title,
            string description,
            T defaultValue,
            IRegistrySerializer? serializer = null,
            SettingOptions options = SettingOptions.None,
            SettingValueTypes? valueType = null)
        {
            var setting = new ConfigurationSetting
            {
                Name = name,
                Title = title,
                Description = description,
                DefaultValue = defaultValue,
                Category = category,
                ValueType = valueType ?? SettingValueTypes.Custom,
                Serializer = serializer ?? new JsonElementSettingSerializer(),
                Options = options
            };
            setting.InitValueWithoutNotify(defaultValue);
            category.AddSetting(setting);
            Settings.Add(setting);
            return setting;
        }

        public ConfigurationCategory? FindCategory(string path) =>
            Categories.FirstOrDefault(c => string.Equals(c.FullPath, path, StringComparison.OrdinalIgnoreCase));

        public ConfigurationSetting? FindSetting(string path) =>
            Settings.FirstOrDefault(s => string.Equals(s.FullPath, path, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<object> Search(string query) => [];
        public IEnumerable<ConfigurationSetting> GetModifiedSettings() => [];
        public void SaveChanges() { }
        public void Reload() { }
        public void Reload(ConfigurationSetting setting) { }
    }
}
