using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class NodePluginLocalizationTest
{
    private string rootPath = null!;

    [SetUp]
    public void SetUp()
    {
        rootPath = Path.Combine(Path.GetTempPath(), $"mytools-plugin-i18n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(rootPath, "locales"));
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
    public void LoadMessages_ShouldMergeDefaultNeutralAndExactLocales()
    {
        var catalogPath = Path.Combine(rootPath, "catalog.json");
        File.WriteAllText(catalogPath, """
        { "entries": [
          { "key": "Plugin.Example.Title", "defaultValue": "English title" },
          { "key": "Plugin.Example.Body", "defaultValue": "English body" }
        ] }
        """);
        File.WriteAllText(Path.Combine(rootPath, "locales", "en-US.json"), """
        { "Plugin.Example.Title": "Default title" }
        """);
        File.WriteAllText(Path.Combine(rootPath, "locales", "fr.json"), """
        { "Plugin.Example.Title": "Titre" }
        """);
        File.WriteAllText(Path.Combine(rootPath, "locales", "fr-CA.json"), """
        {
          "Plugin.Example.Body": "Corps canadien",
          "Host.Forbidden": "Injected"
        }
        """);
        var manifest = CreateManifest(catalogPath);

        var messages = NodePluginLocalization.LoadMessages(manifest, "fr-CA");

        Assert.Multiple(() =>
        {
            Assert.That(messages["Plugin.Example.Title"], Is.EqualTo("Titre"));
            Assert.That(messages["Plugin.Example.Body"], Is.EqualTo("Corps canadien"));
            Assert.That(messages, Does.Not.ContainKey("Host.Forbidden"));
        });
    }

    [Test]
    public void LoadMessages_ShouldFallBackToCatalogForUnknownLocale()
    {
        var catalogPath = Path.Combine(rootPath, "catalog.json");
        File.WriteAllText(catalogPath, """
        { "entries": [{ "key": "Plugin.Example.Title", "defaultValue": "English title" }] }
        """);

        var messages = NodePluginLocalization.LoadMessages(CreateManifest(catalogPath), "de-DE");

        Assert.That(messages["Plugin.Example.Title"], Is.EqualTo("English title"));
    }

    private NodePluginManifest CreateManifest(string catalogPath) => new()
    {
        Id = "example:main",
        ParentId = "example",
        Name = "Example",
        DefaultLocale = "en-US",
        CatalogFullPath = catalogPath,
        LocalesDirectoryFullPath = Path.Combine(rootPath, "locales")
    };
}

