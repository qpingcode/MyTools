using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class NodePluginCatalogTest
{
    private string rootPath = null!;

    [SetUp]
    public void SetUp()
    {
        rootPath = Path.Combine(Path.GetTempPath(), $"mytools-node-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Test]
    public void Reload_ShouldDiscoverValidNodePluginManifest()
    {
        var pluginPath = Path.Combine(rootPath, "hello-search");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "i18n", "locales"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "hello-search",
          "name": "Hello Search",
          "version": "0.2.0",
          "runtime": "node",
          "protocolVersion": "2.0",
          "i18n": {
            "defaultLocale": "en-US",
            "catalog": "i18n/catalog.en-US.json",
            "localesPath": "i18n/locales",
            "supportedLocales": ["en-US", "zh-CN"]
          },
          "entries": [
            {
              "id": "hello",
              "name": { "key": "Plugin.HelloSearch.Name", "defaultValue": "Hello Search" },
              "entry": "backend/index.mjs",
              "keywords": ["hello"],
              "hotKey": "Alt+C",
              "detail": {
                "type": "web",
                "entry": "web/detail.html"
              }
            }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "detail.html"), "<html></html>");
        File.WriteAllText(Path.Combine(pluginPath, "i18n", "catalog.en-US.json"), "{\"entries\":[]}");

        var catalog = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance);

        var plugins = catalog.Reload();

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].Id, Is.EqualTo("hello-search:hello"));
        Assert.That(plugins[0].ParentId, Is.EqualTo("hello-search"));
        Assert.That(plugins[0].EntryId, Is.EqualTo("hello"));
        Assert.That(plugins[0].HotKey, Is.EqualTo("Alt+C"));
        Assert.That(plugins[0].Keywords, Is.EquivalentTo(new[] { "hello" }));
        Assert.That(plugins[0].Capabilities, Is.Empty);
        Assert.That(plugins[0].EntryFullPath, Is.EqualTo(Path.Combine(pluginPath, "backend", "index.mjs")));
        Assert.That(plugins[0].DetailEntryFullPath, Is.EqualTo(Path.Combine(pluginPath, "web", "detail.html")));
        Assert.That(plugins[0].DefaultLocale, Is.EqualTo("en-US"));
        Assert.That(plugins[0].CatalogFullPath, Is.EqualTo(Path.Combine(pluginPath, "i18n", "catalog.en-US.json")));
        Assert.That(plugins[0].LocalesDirectoryFullPath, Is.EqualTo(Path.Combine(pluginPath, "i18n", "locales")));
        Assert.That(plugins[0].SupportedLocales, Is.EquivalentTo(new[] { "en-US", "zh-CN" }));
    }

    [Test]
    public void Reload_ShouldExpandEntriesIntoIndependentNodePlugins()
    {
        var pluginPath = Path.Combine(rootPath, "deepseek-translator");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend", "Translator"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend", "History"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web", "Translator"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web", "History"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "deepseek-translator",
          "name": "DeepSeek Translator",
          "version": "0.1.0",
          "runtime": "node",
          "protocolVersion": "2.0",
          "entries": [
            {
              "id": "translator",
              "name": { "key": "Plugin.Translator.Name", "defaultValue": "DeepSeek Translator" },
              "entry": "backend/Translator/index.mjs",
              "keywords": ["tr", "translate"],
              "hotKey": "Alt+C",
              "detail": {
                "type": "web",
                "entry": "web/Translator/index.html"
              }
            },
            {
              "id": "history",
              "name": { "key": "Plugin.History.Name", "defaultValue": "DeepSeek Translation History" },
              "entry": "backend/History/index.mjs",
              "keywords": ["trh"],
              "hotKey": "Alt+V",
              "detail": {
                "type": "web",
                "entry": "web/History/index.html"
              }
            }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "Translator", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "backend", "History", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "Translator", "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(pluginPath, "web", "History", "index.html"), "<html></html>");

        var catalog = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance);

        var plugins = catalog.Reload();

        Assert.That(plugins, Has.Count.EqualTo(2));
        var translator = plugins.Single(plugin => plugin.EntryId == "translator");
        var history = plugins.Single(plugin => plugin.EntryId == "history");
        Assert.That(translator.Id, Is.EqualTo("deepseek-translator:translator"));
        Assert.That(translator.Keywords, Is.EquivalentTo(new[] { "tr", "translate" }));
        Assert.That(translator.HotKey, Is.EqualTo("Alt+C"));
        Assert.That(translator.EntryFullPath, Is.EqualTo(Path.Combine(pluginPath, "backend", "Translator", "index.mjs")));
        Assert.That(translator.DetailEntryFullPath, Is.EqualTo(Path.Combine(pluginPath, "web", "Translator", "index.html")));
        Assert.That(history.Id, Is.EqualTo("deepseek-translator:history"));
        Assert.That(history.Keywords, Is.EquivalentTo(new[] { "trh" }));
        Assert.That(history.HotKey, Is.EqualTo("Alt+V"));
    }

    [Test]
    public void Reload_ShouldSkipManifestWithMissingBackendEntry()
    {
        var pluginPath = Path.Combine(rootPath, "hello-search");
        Directory.CreateDirectory(pluginPath);
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "hello-search",
          "name": "Hello Search",
          "version": "0.2.0",
          "runtime": "node",
          "protocolVersion": "2.0",
          "entries": [
            {
              "id": "hello",
              "name": "Hello Search",
              "entry": "backend/index.mjs",
              "keywords": ["hello"],
              "detail": {
                "type": "web",
                "entry": "web/detail.html"
              }
            }
          ]
        }
        """);

        var catalog = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance);

        var plugins = catalog.Reload();

        Assert.That(plugins, Is.Empty);
    }

    [Test]
    public void Reload_ShouldSkipLegacySingleEntryManifest()
    {
        var pluginPath = Path.Combine(rootPath, "legacy-search");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "legacy-search",
          "name": "Legacy Search",
          "version": "0.2.0",
          "runtime": "node",
          "entry": "backend/index.mjs",
          "protocolVersion": "2.0",
          "keywords": ["legacy"],
          "detail": {
            "type": "web",
            "entry": "web/detail.html"
          }
        }
        """);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "detail.html"), "<html></html>");

        var catalog = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance);

        var plugins = catalog.Reload();

        Assert.That(plugins, Is.Empty);
    }

    [Test]
    public void Reload_ShouldRejectI18nPathOutsidePluginDirectory()
    {
        var pluginPath = Path.Combine(rootPath, "unsafe");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "");
        File.WriteAllText(Path.Combine(pluginPath, "web", "detail.html"), "");
        File.WriteAllText(Path.Combine(rootPath, "outside.json"), "{\"entries\":[]}");
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "unsafe", "name": "Unsafe", "version": "1.0.0",
          "runtime": "node", "protocolVersion": "2.0",
          "i18n": {
            "defaultLocale": "en-US",
            "catalog": "../outside.json",
            "localesPath": "../"
          },
          "entries": [{
            "id": "main", "entry": "backend/index.mjs", "keywords": ["unsafe"],
            "detail": { "type": "web", "entry": "web/detail.html" }
          }]
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Is.Empty);
    }

    [Test]
    public void Reload_ShouldParseCapabilitiesFromPluginV3Manifest()
    {
        var pluginPath = Path.Combine(rootPath, "settings");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.v3.json"), """
        {
          "id": "settings",
          "version": "0.0.6",
          "protocolVersion": "3.0",
          "entries": [
            {
              "id": "main",
              "name": { "key": "Plugin.Settings.Name", "defaultValue": "Settings" },
              "entry": "backend/index.v3.mjs",
              "capabilities": ["configuration.write"],
              "keywords": ["settings"],
              "detail": {
                "type": "web",
                "entry": "web/index.html"
              }
            }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.v3.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "index.html"), "<html></html>");

        var catalog = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance);
        var plugins = catalog.Reload();

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].Capabilities, Is.EquivalentTo(new[] { "configuration.write" }));
        Assert.That(plugins[0].ProtocolVersion, Is.EqualTo("3.0"));
    }
}
