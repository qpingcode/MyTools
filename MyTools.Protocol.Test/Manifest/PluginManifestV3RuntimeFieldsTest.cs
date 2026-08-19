using System.IO;
using System.Linq;
using System.Text.Json;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Manifest;

/// <summary>
/// Verifies the v3 manifest model carries the runtime fields the WPF app needs (keywords, hotKey,
/// i18n, name) when deserialized from a real plugin.json.
/// </summary>
[TestFixture]
public class PluginManifestV3RuntimeFieldsTest
{
    private static PluginManifestV3 LoadExampleV3(string pluginFolder)
    {
        var root = TestContext.CurrentContext.TestDirectory;
        for (var i = 0; i < 6 && root is not null; i++)
        {
            var path = Path.GetFullPath(Path.Combine(root, "..", "MyTools.Plugins", "Examples", pluginFolder, "plugin.json"));
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<PluginManifestV3>(File.ReadAllText(path), ProtocolJsonOptions.Default)!;
            }
            var candidate = Path.Combine(root, "MyTools.Plugins", "Examples", pluginFolder, "plugin.json");
            if (File.Exists(candidate))
            {
                return JsonSerializer.Deserialize<PluginManifestV3>(File.ReadAllText(candidate), ProtocolJsonOptions.Default)!;
            }
            root = Path.GetDirectoryName(root);
        }
        Assert.Fail($"{pluginFolder} plugin.json not found");
        return null!;
    }

    private static PluginManifestV3 LoadSettingsV3() => LoadExampleV3("settings");

    [Test]
    public void Deserialize_ShouldReadEntryKeywords()
    {
        var m = LoadSettingsV3();
        Assert.That(m.Entries[0].Keywords, Is.Not.Null);
    }

    [Test]
    public void Deserialize_ShouldReadEntryHotKey()
    {
        var m = LoadSettingsV3();
        Assert.That(m.Entries[0].HotKey, Is.EqualTo("Alt+S"));
    }

    [Test]
    public void Deserialize_ShouldReadEntryName()
    {
        var m = LoadSettingsV3();
        Assert.That(m.Entries[0].Name, Is.Not.Null);
        Assert.That(m.Entries[0].Name!.Key, Is.EqualTo("Plugin.Settings.Name"));
        Assert.That(m.Entries[0].Name.DefaultValue, Is.EqualTo("Settings"));
    }

    [Test]
    public void Deserialize_ShouldReadI18n()
    {
        var m = LoadSettingsV3();
        Assert.That(m.I18n, Is.Not.Null);
        Assert.That(m.I18n!.DefaultLocale, Is.EqualTo("en-US"));
        Assert.That(m.I18n.SupportedLocales, Is.EquivalentTo(new[] { "en-US", "zh-CN" }));
    }

    [Test]
    public void Deserialize_ShouldReadDetailEntry()
    {
        var m = LoadSettingsV3();
        Assert.That(m.Entries[0].Detail, Is.Not.Null);
        Assert.That(m.Entries[0].Detail!.Type, Is.EqualTo("web"));
        Assert.That(m.Entries[0].Detail.Entry, Is.EqualTo("web/index.html"));
    }

    [Test]
    public void RoundTrip_ShouldPreserveKeywordsAndHotKey()
    {
        var m = LoadSettingsV3();
        var json = JsonSerializer.Serialize(m, ProtocolJsonOptions.Default);
        var back = JsonSerializer.Deserialize<PluginManifestV3>(json, ProtocolJsonOptions.Default)!;

        Assert.That(back.Entries[0].HotKey, Is.EqualTo(m.Entries[0].HotKey));
        Assert.That(back.Entries[0].Keywords, Is.EquivalentTo(m.Entries[0].Keywords!));
    }

    [Test]
    public void Deserialize_ShouldReadEntrySearchGlobal()
    {
        var hello = LoadExampleV3("hello-search");
        Assert.That(hello.Entries[0].Search, Is.Not.Null);
        Assert.That(hello.Entries[0].Search!.Global, Is.True);

        var settings = LoadSettingsV3();
        Assert.That(settings.Entries[0].Search, Is.Null);
    }

    [Test]
    public void Deserialize_ShouldReadPluginConfiguration()
    {
        var snippet = LoadExampleV3("snippet");
        Assert.That(snippet.Configuration, Has.Count.EqualTo(1));
        Assert.That(snippet.Configuration[0].Key, Is.EqualTo("Phrases"));
        Assert.That(snippet.Configuration[0].Type, Is.EqualTo("array"));
        Assert.That(snippet.Configuration[0].Schema!.Properties, Has.Count.EqualTo(3));
        Assert.That(snippet.Icon, Is.EqualTo("mdi-message-text-outline"));
        Assert.That(snippet.Entries[0].Capabilities, Is.EquivalentTo(new[] { "configuration.readOwn" }));
        Assert.That(snippet.Entries[0].Search!.Global, Is.True);
    }
}
