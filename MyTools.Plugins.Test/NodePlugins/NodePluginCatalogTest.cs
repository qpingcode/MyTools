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
          "id": "hello",
          "name": {
            "key": "Plugin.HelloSearch.Name",
            "defaultValue": "Hello Search"
          },
          "version": "0.2.0",
          "runtime": "node",
          "protocolVersion": "3.0",
          "i18n": {
            "defaultLocale": "en-US",
            "catalog": "i18n/catalog.en-US.json",
            "localesPath": "i18n/locales",
            "supportedLocales": [
              "en-US",
              "zh-CN"
            ]
          },
          "entry": "backend/index.mjs",
          "alias": [
            "hello"
          ],
          "hotKey": "Alt+C",
          "window": {
            "showStatusBar": false
          },
          "detail": {
            "type": "web",
            "entry": "web/detail.html"
          }
        }
        """);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "detail.html"), "<html></html>");
        File.WriteAllText(Path.Combine(pluginPath, "i18n", "catalog.en-US.json"), "{\"entries\":[]}");

        var catalog = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance);

        var plugins = catalog.Reload();

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].Id, Is.EqualTo("hello"));
        Assert.That(plugins[0].ParentId, Is.EqualTo("hello"));
        Assert.That(plugins[0].HotKey, Is.EqualTo("Alt+C"));
        Assert.That(plugins[0].Keywords, Is.EquivalentTo(new[] { "hello" }));
        Assert.That(plugins[0].SearchGlobal, Is.False);
        Assert.That(plugins[0].ShowStatusBarInPluginWindow, Is.False);
        Assert.That(plugins[0].Capabilities, Is.Empty);
        Assert.That(plugins[0].EntryFullPath, Is.EqualTo(Path.Combine(pluginPath, "backend", "index.mjs")));
        Assert.That(plugins[0].DetailEntryFullPath, Is.EqualTo(Path.Combine(pluginPath, "web", "detail.html")));
        Assert.That(plugins[0].DefaultLocale, Is.EqualTo("en-US"));
        Assert.That(plugins[0].CatalogFullPath, Is.EqualTo(Path.Combine(pluginPath, "i18n", "catalog.en-US.json")));
        Assert.That(plugins[0].LocalesDirectoryFullPath, Is.EqualTo(Path.Combine(pluginPath, "i18n", "locales")));
        Assert.That(plugins[0].SupportedLocales, Is.EquivalentTo(new[] { "en-US", "zh-CN" }));
    }

    [Test]
    public void Reload_ShouldRejectMultipleEntriesManifest()
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
          "protocolVersion": "3.0",
          "entries": [
            {
              "id": "translator",
              "name": { "key": "Plugin.Translator.Name", "defaultValue": "DeepSeek Translator" },
              "entry": "backend/Translator/index.mjs",
              "alias": ["tr", "translate"],
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
              "alias": ["trh"],
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

        Assert.That(plugins, Is.Empty);
    }

    [Test]
    public void Reload_ShouldSkipManifestWithMissingBackendEntry()
    {
        var pluginPath = Path.Combine(rootPath, "hello-search");
        Directory.CreateDirectory(pluginPath);
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "hello",
          "name": "Hello Search",
          "version": "0.2.0",
          "runtime": "node",
          "protocolVersion": "3.0",
          "entry": "backend/index.mjs",
          "alias": [
            "hello"
          ],
          "detail": {
            "type": "web",
            "entry": "web/detail.html"
          }
        }
        """);

        var catalog = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance);

        var plugins = catalog.Reload();

        Assert.That(plugins, Is.Empty);
    }

    [Test]
    public void Reload_ShouldLoadSingleEntryManifest()
    {
        var pluginPath = Path.Combine(rootPath, "legacy-search");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "legacy-search",
          "version": "0.2.0",
          "runtime": "node",
          "entry": "backend/index.mjs",
          "protocolVersion": "3.0",
          "alias": ["legacy"],
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

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].Id, Is.EqualTo("legacy-search"));
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
          "id": "main",
          "name": "Unsafe",
          "version": "1.0.0",
          "runtime": "node",
          "protocolVersion": "3.0",
          "i18n": {
            "defaultLocale": "en-US",
            "catalog": "../outside.json",
            "localesPath": "../"
          },
          "entry": "backend/index.mjs",
          "alias": [
            "unsafe"
          ],
          "detail": {
            "type": "web",
            "entry": "web/detail.html"
          }
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Is.Empty);
    }

    [Test]
    public void Reload_ShouldParseCapabilitiesFromPluginManifest()
    {
        var pluginPath = Path.Combine(rootPath, "settings");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "main",
          "version": "0.0.6",
          "protocolVersion": "3.0",
          "name": {
            "key": "Plugin.Settings.Name",
            "defaultValue": "Settings"
          },
          "entry": "backend/index.mjs",
          "capabilities": [
            "configuration.write"
          ],
          "alias": [
            "settings"
          ],
          "detail": {
            "type": "web",
            "entry": "web/index.html"
          }
        }
        """);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "index.html"), "<html></html>");

        var catalog = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance);
        var plugins = catalog.Reload();

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].Capabilities, Is.EquivalentTo(new[] { "configuration.write" }));
        Assert.That(plugins[0].ProtocolVersion, Is.EqualTo("3.0"));
    }

    [Test]
    public void Reload_ShouldParseExplicitSearchLevels()
    {
        var pluginPath = Path.Combine(rootPath, "hello-search");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "hello",
          "version": "0.2.0",
          "protocolVersion": "3.0",
          "name": {
            "key": "Plugin.HelloSearch.Name",
            "defaultValue": "Hello Search"
          },
          "entry": "backend/index.mjs",
          "alias": [],
          "search": {
            "global": true
          },
          "detail": {
            "type": "web",
            "entry": "web/index.html"
          }
        }
        """);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "index.html"), "<html></html>");

        var catalog = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance);
        var plugins = catalog.Reload();

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].SearchGlobal, Is.True);
    }

    [Test]
    public void Reload_ShouldDefaultGlobalSearchOffWhenSearchOmitted()
    {
        var pluginPath = Path.Combine(rootPath, "no-kw");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "main",
          "version": "0.1.0",
          "protocolVersion": "3.0",
          "entry": "backend/index.mjs",
          "detail": {
            "type": "web",
            "entry": "web/index.html"
          }
        }
        """);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "index.html"), "<html></html>");

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].SearchGlobal, Is.False);
        Assert.That(plugins[0].ShowStatusBarInPluginWindow, Is.True);
    }

    [Test]
    public void Reload_ShouldSkipProtocolVersionOtherThan3()
    {
        var pluginPath = Path.Combine(rootPath, "old");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "main",
          "version": "0.1.0",
          "protocolVersion": "2.0",
          "entry": "backend/index.mjs",
          "detail": {
            "type": "web",
            "entry": "web/index.html"
          }
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Is.Empty);
    }

    [Test]
    public void Reload_ShouldLoadEntryWhenDetailOmitted()
    {
        var pluginPath = WriteBackendOnlyPlugin("""
        {
          "id": "main",
          "version": "0.1.0",
          "protocolVersion": "3.0",
          "name": {
            "key": "Plugin.List.Name",
            "defaultValue": "List Plugin"
          },
          "entry": "backend/index.mjs",
          "alias": [
            "list"
          ]
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].Id, Is.EqualTo("main"));
        Assert.That(plugins[0].DetailEntry, Is.Null);
        Assert.That(plugins[0].DetailEntryFullPath, Is.Null);
        Assert.That(plugins[0].HasWebDetail, Is.False);
        Assert.That(plugins[0].EntryFullPath, Is.EqualTo(Path.Combine(pluginPath, "backend", "index.mjs")));
    }

    [Test]
    public void Reload_ShouldLoadEntryWhenDetailTypeIsList()
    {
        WriteBackendOnlyPlugin("""
        {
          "id": "main",
          "version": "0.1.0",
          "protocolVersion": "3.0",
          "entry": "backend/index.mjs",
          "detail": {
            "type": "list"
          }
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].HasWebDetail, Is.False);
        Assert.That(plugins[0].DetailEntryFullPath, Is.Null);
    }

    [Test]
    public void Reload_ShouldSkipBasicDetailType()
    {
        WriteBackendOnlyPlugin("""
        {
          "id": "main",
          "version": "0.1.0",
          "protocolVersion": "3.0",
          "entry": "backend/index.mjs",
          "detail": {
            "type": "basic"
          }
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Is.Empty);
    }

    [Test]
    public void Reload_ShouldSkipWebDetailWithoutEntry()
    {
        WriteBackendOnlyPlugin("""
        {
          "id": "main",
          "version": "0.1.0",
          "protocolVersion": "3.0",
          "entry": "backend/index.mjs",
          "detail": {
            "type": "web"
          }
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Is.Empty);
    }

    [Test]
    public void Reload_ShouldParsePluginConfigurationSchema()
    {
        WriteBackendOnlyPlugin("""
        {
          "id": "snippet",
          "version": "0.1.0",
          "protocolVersion": "3.0",
          "icon": "mdi-message-text-outline",
          "configuration": [
            {
              "key": "Phrases",
              "label": {
                "key": "Plugin.Snippet.Setting.Phrases",
                "defaultValue": "Phrases"
              },
              "type": "array",
              "defaultValue": [],
              "schema": {
                "properties": [
                  {
                    "key": "trigger",
                    "type": "string"
                  },
                  {
                    "key": "content",
                    "type": "string",
                    "uiHint": "textarea"
                  }
                ]
              }
            }
          ],
          "name": {
            "key": "Plugin.Snippet.Name",
            "defaultValue": "Snippet"
          },
          "entry": "backend/index.mjs",
          "capabilities": [
            "configuration.readOwn"
          ],
          "search": {
            "global": true
          }
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Has.Count.EqualTo(1));
        Assert.That(plugins[0].Capabilities, Is.EquivalentTo(new[] { "configuration.readOwn" }));
        Assert.That(plugins[0].Icon, Is.EqualTo("mdi-message-text-outline"));
        Assert.That(plugins[0].Configuration, Has.Count.EqualTo(1));
        Assert.That(plugins[0].Configuration[0].Key, Is.EqualTo("Phrases"));
        Assert.That(plugins[0].Configuration[0].Type, Is.EqualTo("array"));
        Assert.That(plugins[0].Configuration[0].Schema!.Properties, Has.Count.EqualTo(2));
    }

    [Test]
    public void Reload_ShouldSkipPluginWithInvalidConfiguration()
    {
        WriteBackendOnlyPlugin("""
        {
          "id": "main",
          "version": "0.1.0",
          "protocolVersion": "3.0",
          "configuration": [
            {
              "key": "Items",
              "type": "array"
            }
          ],
          "entry": "backend/index.mjs"
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Is.Empty);
    }

    private string WriteBackendOnlyPlugin(string pluginJson)
    {
        var pluginPath = Path.Combine(rootPath, "list-plugin");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), pluginJson);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "console.log('ok');");
        return pluginPath;
    }
}
