using System.Text.Json;
using MyTools.Common.Config.Enums;
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
            Name = "MaxHistory",
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

    private static ConfigurationSetting CreateArraySetting()
    {
        return new ConfigurationSetting
        {
            Name = "Phrases",
            ValueType = SettingValueTypes.Array,
            Serializer = new JsonElementSettingSerializer()
        };
    }
}
