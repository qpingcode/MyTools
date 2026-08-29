using System.Text.Json;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Plugins;
using MyTools.Desktop.Services;
using MyTools.Desktop.Serializers;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class ConfigurationSettingValuesTest
{
    [Test]
    public void Owns_ShouldMatchPluginId()
    {
        Assert.That(ConfigurationSettingValues.Owns(new PluginId("snippet"), CreateOwned("snippet", "Phrases")), Is.True);
        Assert.That(ConfigurationSettingValues.Owns(new PluginId("snippet"), CreateOwned("openpath", "RiderInstallPath")), Is.False);
    }

    [Test]
    public void Convert_Array_ShouldParseJsonArray()
    {
        var setting = CreateArraySetting();
        var value = ConfigurationSettingValues.Convert(setting, """[{"trigger":"sig","content":"Hello"}]""");

        Assert.That(value, Is.TypeOf<JsonElement>());
        var json = (JsonElement)value!;
        Assert.That(json.GetArrayLength(), Is.EqualTo(1));
        Assert.That(json[0].GetProperty("trigger").GetString(), Is.EqualTo("sig"));
    }

    [Test]
    public void Convert_Array_ShouldRejectNonArrayJson()
    {
        var setting = CreateArraySetting();
        Assert.That(
            () => ConfigurationSettingValues.Convert(setting, """{"trigger":"sig"}"""),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Convert_Integer_EmptyOrInvalid_ShouldUseDefault()
    {
        var setting = new ConfigurationSetting
        {
            Name = "IntegerValue",
            ValueType = SettingValueTypes.Integer,
            DefaultValue = 100,
            Serializer = new IntegerSerializer()
        };

        Assert.That(ConfigurationSettingValues.Convert(setting, ""), Is.EqualTo(100));
        Assert.That(ConfigurationSettingValues.Convert(setting, "   "), Is.EqualTo(100));
        Assert.That(ConfigurationSettingValues.Convert(setting, "abc"), Is.EqualTo(100));
        Assert.That(ConfigurationSettingValues.Convert(setting, "12"), Is.EqualTo(12));
    }

    [Test]
    public void Convert_Double_EmptyOrInvalid_ShouldUseDefault()
    {
        var setting = new ConfigurationSetting
        {
            Name = "SearchDelay",
            ValueType = SettingValueTypes.Double,
            DefaultValue = 250.0,
            Serializer = new DoubleSerializer()
        };

        Assert.That(ConfigurationSettingValues.Convert(setting, ""), Is.EqualTo(250.0));
        Assert.That(ConfigurationSettingValues.Convert(setting, "80"), Is.EqualTo(80.0));
    }

    [Test]
    public void Convert_String_Empty_ShouldRemainEmpty()
    {
        var setting = new ConfigurationSetting
        {
            Name = "UpdateProxyUrl",
            ValueType = SettingValueTypes.String,
            DefaultValue = string.Empty,
            Serializer = new StringSerializer()
        };

        Assert.That(ConfigurationSettingValues.Convert(setting, ""), Is.EqualTo(""));
    }

    [Test]
    public void ToDtoString_ShouldKeepArrayJson()
    {
        using var document = JsonDocument.Parse("""[{"a":1}]""");
        Assert.That(ConfigurationSettingValues.ToDtoString(document.RootElement.Clone()), Is.EqualTo("""[{"a":1}]"""));
        Assert.That(ConfigurationSettingValues.ToDtoString(true), Is.EqualTo("True"));
    }

    [Test]
    public void ConvertOwnedJson_Array_ShouldCloneJsonArray()
    {
        var setting = CreateArraySetting();
        using var document = JsonDocument.Parse("""[{"name":"Google","url":"https://www.google.com/search?q={query}"}]""");

        var value = ConfigurationSettingValues.ConvertOwnedJson(setting, document.RootElement);
        Assert.That(value, Is.TypeOf<JsonElement>());
        var json = (JsonElement)value!;
        Assert.That(json.GetArrayLength(), Is.EqualTo(1));
        Assert.That(json[0].GetProperty("name").GetString(), Is.EqualTo("Google"));
    }

    [Test]
    public void ApplyOwnedValues_ShouldWriteOnlyTheCallingPlugin()
    {
        var registry = new FakeRegistry();
        var searchEngine = registry.AddArraySetting("search-engine", "Engines");
        var snippet = registry.AddArraySetting("snippet", "Phrases");
        using var document = JsonDocument.Parse("""{"Engines":[{"name":"Google"}],"Phrases":[{"trigger":"x"}]}""");

        var applied = ConfigurationSettingValues.ApplyOwnedValues(
            registry,
            new PluginId("search-engine"),
            document.RootElement);

        Assert.That(applied, Is.EqualTo(1));
        Assert.That(((JsonElement)searchEngine.CurrentValue!).GetArrayLength(), Is.EqualTo(1));
        Assert.That(((JsonElement)searchEngine.CurrentValue).GetRawText(), Does.Contain("Google"));
        Assert.That(((JsonElement)snippet.CurrentValue!).GetRawText(), Is.EqualTo("[]"));
    }

    private static ConfigurationSetting CreateOwned(string pluginId, string name)
    {
        var category = new ConfigurationCategory { Key = pluginId, Name = pluginId, PluginId = new PluginId(pluginId) };
        return new ConfigurationSetting
        {
            Key = $"{pluginId}.{name}",
            Name = name,
            PluginId = new PluginId(pluginId),
            Category = category,
            ValueType = SettingValueTypes.String,
            Serializer = new StringSerializer()
        };
    }

    private static ConfigurationSetting CreateArraySetting()
    {
        return new ConfigurationSetting
        {
            Key = "snippet.Phrases",
            Name = "Phrases",
            ValueType = SettingValueTypes.Array,
            Serializer = new JsonElementSettingSerializer()
        };
    }

    private sealed class FakeRegistry : IConfigurationRegistry
    {
        public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
        {
            add { }
            remove { }
        }

        public List<ConfigurationSetting> Settings { get; } = new();

        public ConfigurationSetting AddArraySetting(string pluginId, string name)
        {
            var category = new ConfigurationCategory { Key = pluginId, Name = pluginId, PluginId = new PluginId(pluginId) };
            var setting = new ConfigurationSetting
            {
                Key = $"{pluginId}.{name}",
                Name = name,
                PluginId = new PluginId(pluginId),
                Category = category,
                ValueType = SettingValueTypes.Array,
                Serializer = new JsonElementSettingSerializer()
            };
            setting.InitValueWithoutNotify(JsonSerializer.SerializeToElement(Array.Empty<object>()));
            Settings.Add(setting);
            return setting;
        }

        public ConfigurationCategory AddCategory(
            string key,
            string name,
            string description,
            bool IsSelectable = true,
            PluginId? pluginId = null) =>
            throw new NotSupportedException();

        public ConfigurationSetting AddSetting<T>(
            ConfigurationCategory category,
            string name,
            string title,
            string description,
            T defaultValue,
            IRegistrySerializer? serializer = null,
            SettingOptions options = SettingOptions.None,
            SettingValueTypes? valueType = null) =>
            throw new NotSupportedException();

        public IEnumerable<ConfigurationCategory> GetRootCategories() => [];
        public ConfigurationCategory? FindCategory(string path) => null;
        public ConfigurationSetting? FindSetting(string key) =>
            Settings.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        public bool RemoveCategory(string path) => false;
        public void SaveChanges() { }
        public void Reload() { }
        public void Reload(ConfigurationSetting setting) { }
    }
}
