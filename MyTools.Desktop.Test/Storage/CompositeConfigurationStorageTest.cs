using System.IO;
using System.Text;
using MyTools.Common.Config;
using MyTools.Common.Plugins;
using MyTools.Desktop.Services;
using MyTools.Desktop.Storage;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Storage;

[TestFixture]
public class CompositeConfigurationStorageTest
{
    private string tempRoot = null!;
    private string hostPath = null!;
    private string pluginsDataRoot = null!;
    private static readonly PluginId QuickText = new("quick-text");
    private static readonly PluginId SearchEngine = new("search-engine");
    private static readonly PluginId Clipboard = new("clipboard");

    [SetUp]
    public void SetUp()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "MyToolsConfig-" + Guid.NewGuid().ToString("N"));
        hostPath = Path.Combine(tempRoot, "Settings.json");
        pluginsDataRoot = Path.Combine(tempRoot, "pluginsData");
        Directory.CreateDirectory(tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Store_HostSetting_StaysInSettingsJson()
    {
        using var storage = CreateStorage();
        storage.Store("General.Theme", "dark");

        Assert.Multiple(() =>
        {
            Assert.That(storage.Retrieve("General.Theme"), Is.EqualTo("dark"));
            Assert.That(File.Exists(hostPath), Is.True);
            Assert.That(File.ReadAllText(hostPath), Does.Contain("General.Theme"));
            Assert.That(Directory.Exists(pluginsDataRoot), Is.False);
        });
    }

    [Test]
    public void Store_PluginSetting_GoesToPluginSettingsFile()
    {
        using var storage = CreateStorage();
        storage.Store("Phrases", """[{"trigger":"sig"}]""", QuickText);

        var pluginSettingsPath = ConfigPath.PluginSettingsPath(pluginsDataRoot, QuickText.Value);
        Assert.Multiple(() =>
        {
            Assert.That(storage.Retrieve("Phrases", QuickText), Is.EqualTo("""[{"trigger":"sig"}]"""));
            Assert.That(storage.Retrieve("Phrases"), Is.Null);
            Assert.That(File.Exists(pluginSettingsPath), Is.True);
            Assert.That(File.ReadAllText(pluginSettingsPath), Does.Contain("\"name\": \"Phrases\""));
            Assert.That(File.ReadAllText(pluginSettingsPath), Does.Not.Contain("quick-text.Phrases"));
            Assert.That(File.Exists(hostPath), Is.False);
        });
    }

    [Test]
    public void Store_DoesNotLeavePluginKeysInHostFile()
    {
        using var storage = CreateStorage();
        storage.Store("General.Language", "en-US");
        storage.Store("Engines", """[{"name":"Google"}]""", SearchEngine);

        Assert.That(File.ReadAllText(hostPath), Does.Not.Contain("search-engine"));
        Assert.That(File.ReadAllText(hostPath), Does.Contain("General.Language"));
    }

    [Test]
    public void Retrieve_MigratesLegacyPluginKeysOutOfHostSettings()
    {
        WriteHostSettings(
            ("General.Theme", "light"),
            ("quick-text.Phrases", "legacy-phrases"),
            ("ClipBoard.HotKey", "Ctrl+Shift+V"));

        using var storage = CreateStorage();

        Assert.Multiple(() =>
        {
            Assert.That(storage.Retrieve("General.Theme"), Is.EqualTo("light"));
            Assert.That(storage.Retrieve("Phrases", QuickText), Is.EqualTo("legacy-phrases"));
            Assert.That(storage.Retrieve("HotKey", Clipboard), Is.EqualTo("Ctrl+Shift+V"));
            Assert.That(File.ReadAllText(hostPath), Does.Contain("General.Theme"));
            Assert.That(File.ReadAllText(hostPath), Does.Not.Contain("quick-text"));
            Assert.That(File.ReadAllText(hostPath), Does.Not.Contain("clipboard"));
            Assert.That(File.Exists(ConfigPath.PluginSettingsPath(pluginsDataRoot, QuickText.Value)), Is.True);
            Assert.That(File.Exists(ConfigPath.PluginSettingsPath(pluginsDataRoot, Clipboard.Value)), Is.True);
        });
    }

    [Test]
    public void Retrieve_HostSetting_DoesNotCreatePluginData()
    {
        WriteHostSettings(
            ("General.Theme", "light"),
            ("UI.FontSize", "141"));

        using var storage = CreateStorage();

        Assert.Multiple(() =>
        {
            Assert.That(storage.Retrieve("UI.FontSize"), Is.EqualTo("141"));
            Assert.That(File.ReadAllText(hostPath), Does.Contain("UI.FontSize"));
            Assert.That(Directory.Exists(pluginsDataRoot), Is.False);
        });
    }

    [Test]
    public void Retrieve_DoesNotOverwriteExistingPluginFile()
    {
        WriteHostSettings(("quick-text.Phrases", "from-host"));
        var pluginSettingsPath = ConfigPath.PluginSettingsPath(pluginsDataRoot, QuickText.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(pluginSettingsPath)!);
        File.WriteAllText(pluginSettingsPath, """
            [
              { "name": "Phrases", "value": "from-plugin", "lastModified": "2026-01-01T00:00:00Z" }
            ]
            """);

        using var storage = CreateStorage();

        Assert.That(storage.Retrieve("Phrases", QuickText), Is.EqualTo("from-plugin"));
        Assert.That(File.ReadAllText(hostPath), Does.Not.Contain("quick-text"));
    }

    [Test]
    public void GetAllNames_IsScopedByPluginId()
    {
        using var storage = CreateStorage();
        storage.Store("General.Theme", "dark");
        storage.Store("Phrases", "[]", QuickText);
        storage.Store("Gestures.EnableGesture", "true");

        Assert.Multiple(() =>
        {
            Assert.That(storage.GetAllNames(), Is.EquivalentTo(new[] { "General.Theme", "Gestures.EnableGesture" }));
            Assert.That(storage.GetAllNames(QuickText), Is.EquivalentTo(new[] { "Phrases" }));
        });
    }

    [Test]
    public void Delete_RemovesPluginSettingWithoutTouchingHost()
    {
        using var storage = CreateStorage();
        storage.Store("General.Theme", "dark");
        storage.Store("Phrases", "[]", QuickText);

        storage.Delete("Phrases", QuickText);

        Assert.Multiple(() =>
        {
            Assert.That(storage.Exists("Phrases", QuickText), Is.False);
            Assert.That(storage.Exists("General.Theme"), Is.True);
        });
    }

    [Test]
    public void Reload_NewInstanceReadsPluginFile()
    {
        using (var storage = CreateStorage())
        {
            storage.Store("Phrases", "hello", QuickText);
        }

        using var reloaded = CreateStorage();
        Assert.That(reloaded.Retrieve("Phrases", QuickText), Is.EqualTo("hello"));
    }

    [Test]
    public void ConfigurationRegistry_SavesPluginSettingToPluginData()
    {
        using var storage = CreateStorage();
        var registry = new ConfigurationRegistry(storage);
        var category = registry.AddCategory("quick-text", "Quick Text", "", pluginId: QuickText);
        var setting = registry.AddSetting(category, "Phrases", "Phrases", "", "[]", serializer: null);
        setting.CurrentValue = """[{"trigger":"sig"}]""";
        registry.SaveChanges();

        var pluginSettingsPath = ConfigPath.PluginSettingsPath(pluginsDataRoot, QuickText.Value);
        Assert.Multiple(() =>
        {
            Assert.That(setting.PluginId, Is.EqualTo(QuickText));
            Assert.That(setting.StorageKey, Is.EqualTo("Phrases"));
            Assert.That(File.Exists(pluginSettingsPath), Is.True);
            Assert.That(File.ReadAllText(pluginSettingsPath), Does.Contain("sig"));
            Assert.That(File.Exists(hostPath), Is.False);
        });
    }

    private CompositeConfigurationStorage CreateStorage() =>
        new(new JsonConfigurationStorage(hostPath), pluginsDataRoot);

    private void WriteHostSettings(params (string Name, string Value)[] settings)
    {
        var json = new StringBuilder();
        json.AppendLine("[");
        for (var i = 0; i < settings.Length; i++)
        {
            var escaped = settings[i].Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            json.Append(
                $"  {{ \"name\": \"{settings[i].Name}\", \"value\": \"{escaped}\", \"lastModified\": \"2026-01-01T00:00:00Z\" }}");
            json.AppendLine(i < settings.Length - 1 ? "," : "");
        }

        json.AppendLine("]");
        File.WriteAllText(hostPath, json.ToString());
    }
}
