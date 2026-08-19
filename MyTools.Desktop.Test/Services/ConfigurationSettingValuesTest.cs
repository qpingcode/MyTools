using System.Text.Json;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Models;
using MyTools.Desktop.Services;
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
