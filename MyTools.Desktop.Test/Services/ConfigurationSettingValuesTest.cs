using System.Text.Json;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Desktop.Services;
using MyTools.Desktop.Serializers;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class ConfigurationSettingValuesTest
{
    [Test]
    public void Owns_ShouldMatchPluginPrefixOnly()
    {
        Assert.That(ConfigurationSettingValues.Owns("snippet", "snippet.Phrases"), Is.True);
        Assert.That(ConfigurationSettingValues.Owns("snippet", "openpath.RiderInstallPath"), Is.False);
        Assert.That(ConfigurationSettingValues.Owns("snippet", "snippet"), Is.False);
        Assert.That(ConfigurationSettingValues.Owns("", "snippet.Phrases"), Is.False);
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

        var applied = ConfigurationSettingValues.ApplyOwnedValues(registry, "search-engine", document.RootElement);

        Assert.That(applied, Is.EqualTo(1));
        Assert.That(((JsonElement)searchEngine.CurrentValue!).GetArrayLength(), Is.EqualTo(1));
        Assert.That(((JsonElement)searchEngine.CurrentValue).GetRawText(), Does.Contain("Google"));
        Assert.That(((JsonElement)snippet.CurrentValue!).GetRawText(), Is.EqualTo("[]"));
    }

    private static ConfigurationSetting CreateArraySetting()
    {
        return new ConfigurationSetting
        {
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

        public List<ConfigurationSetting> Settings { get; } = [];

        public ConfigurationSetting AddArraySetting(string pluginId, string name)
        {
            var category = new ConfigurationCategory { Key = pluginId, Name = pluginId };
            var setting = new ConfigurationSetting
            {
                Name = name,
                Category = category,
                ValueType = SettingValueTypes.Array,
                Serializer = new JsonElementSettingSerializer()
            };
            setting.InitValueWithoutNotify(JsonSerializer.SerializeToElement(Array.Empty<object>()));
            Settings.Add(setting);
            return setting;
        }

        public ConfigurationCategory AddCategory(string name, string description, ConfigurationCategory? parent = null, bool IsSelectable = true) =>
            throw new NotSupportedException();

        public ConfigurationCategory AddCategory(string key, string name, string description, ConfigurationCategory? parent = null, bool IsSelectable = true) =>
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
        public ConfigurationSetting? FindSetting(string path) =>
            Settings.FirstOrDefault(s => string.Equals(s.FullPath, path, StringComparison.OrdinalIgnoreCase));
        public bool RemoveCategory(string path) => false;
        public IEnumerable<object> Search(string query) => [];
        public IEnumerable<ConfigurationSetting> GetModifiedSettings() => [];
        public void SaveChanges() { }
        public void Reload() { }
        public void Reload(ConfigurationSetting setting) { }
    }
}
