using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Common.Localization;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class NodePluginKeywordRouteTest
{
    private string rootPath = null!;
    private string backendPath = null!;
    private string detailPath = null!;

    [SetUp]
    public void SetUp()
    {
        rootPath = Path.Combine(Path.GetTempPath(), $"mytools-node-plugin-route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        backendPath = Path.Combine(rootPath, "index.mjs");
        detailPath = Path.Combine(rootPath, "web", "index.html");
        Directory.CreateDirectory(Path.GetDirectoryName(detailPath)!);
        File.WriteAllText(backendPath, "console.log('ok');");
        File.WriteAllText(detailPath, "<html></html>");
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
    public void ShouldOpenDetailOnKeywordRoute_ShouldOpenOnExactKeyword()
    {
        using var plugin = CreatePlugin();

        Assert.That(plugin.ShouldOpenDetailOnKeywordRoute("tr"), Is.True);
        Assert.That(plugin.ShouldOpenDetailOnKeywordRoute("tr hello"), Is.True);
        Assert.That(plugin.ShouldOpenDetailOnKeywordRoute("trash"), Is.False);
    }

    [Test]
    public void CreateHotKeyDetailContext_ShouldUseIndependentPluginId()
    {
        using var plugin = CreatePlugin();

        var context = plugin.CreateHotKeyDetailContext();

        Assert.That(context, Is.Not.Null);
        Assert.That(context!.PluginId, Is.EqualTo("deepseek-translator:translator"));
        Assert.That(context.Keyword, Is.EqualTo("tr"));
        Assert.That(context.SearchText, Is.EqualTo("tr "));
        Assert.That(context.EntryFullPath, Is.EqualTo(detailPath));
    }

    private NodePlugin CreatePlugin()
    {
        var manifest = new NodePluginManifest
        {
            Id = "deepseek-translator:translator",
            ParentId = "deepseek-translator",
            EntryId = "translator",
            NameMessage = new LocalizedMessage("DeepSeek key", "DeepSeek Translator"),
            Version = "0.1.0",
            Runtime = "node",
            Entry = "index.mjs",
            ProtocolVersion = "2.0",
            PluginDirectory = rootPath,
            EntryFullPath = backendPath,
            DetailEntry = Path.Combine("web", "index.html"),
            DetailEntryFullPath = detailPath,
            Keywords = ["tr"],
            HotKey = "Alt+C"
        };

        return new NodePluginFactory(NullLoggerFactory.Instance).CreatePlugins([manifest]).Single();
    }
}
