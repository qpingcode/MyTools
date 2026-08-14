using System.IO;
using System.Linq;
using System.Text.Json;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Manifest;

/// <summary>
/// Verifies the v3 manifest model carries the runtime fields the WPF app needs (keywords, hotKey,
/// i18n, name) when deserialized from a real plugin.v3.json.
/// </summary>
[TestFixture]
public class PluginManifestV3RuntimeFieldsTest
{
    private static PluginManifestV3 LoadSettingsV3()
    {
        var root = TestContext.CurrentContext.TestDirectory;
        for (var i = 0; i < 6 && root is not null; i++)
        {
            var path = Path.Combine(root, "..", "MyTools.Plugins", "Examples", "settings", "plugin.v3.json");
            path = Path.GetFullPath(path);
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<PluginManifestV3>(File.ReadAllText(path), ProtocolJsonOptions.Default)!;
            }
            root = Path.GetDirectoryName(root);
        }
        // Fallback: search the repo for the file.
        var dir = TestContext.CurrentContext.TestDirectory;
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "MyTools.Plugins", "Examples", "settings", "plugin.v3.json");
            if (File.Exists(candidate))
            {
                return JsonSerializer.Deserialize<PluginManifestV3>(File.ReadAllText(candidate), ProtocolJsonOptions.Default)!;
            }
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("settings plugin.v3.json not found");
        return null!;
    }

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
}
