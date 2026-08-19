using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Common.Localization;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
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
    public void KeywordRoute_ShouldUseEffectiveKeywordOverride()
    {
        using var plugin = CreatePlugin();
        plugin.SetEffectiveKeywords(["translate"]);

        Assert.That(plugin.ShouldOpenDetailOnKeywordRoute("tr hello"), Is.False);
        Assert.That(plugin.ShouldOpenDetailOnKeywordRoute("translate hello"), Is.True);
        Assert.That(plugin.GetQueryWithoutKeyword("translate hello"), Is.EqualTo("hello"));

        var context = plugin.CreateKeywordDetailContext("translate hello", "hello");
        Assert.That(context, Is.Not.Null);
        Assert.That(context!.Keyword, Is.EqualTo("translate"));
    }

    [Test]
    public void ShouldOpenDetailOnKeywordRoute_ShouldStayOnListWhenDetailOmitted()
    {
        using var plugin = CreatePlugin(withWebDetail: false);

        Assert.That(plugin.ShouldOpenDetailOnKeywordRoute("tr"), Is.False);
        Assert.That(plugin.ShouldOpenDetailOnKeywordRoute("tr hello"), Is.False);
    }

    [Test]
    public void CreateHotKeyDetailContext_ShouldReturnNullWhenDetailOmitted()
    {
        using var plugin = CreatePlugin(withWebDetail: false);

        Assert.That(plugin.CreateHotKeyDetailContext(), Is.Null);
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

    private NodePlugin CreatePlugin(bool withWebDetail = true)
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
            ProtocolVersion = "3.0",
            PluginDirectory = rootPath,
            EntryFullPath = backendPath,
            DetailEntry = withWebDetail ? Path.Combine("web", "index.html") : null,
            DetailEntryFullPath = withWebDetail ? detailPath : null,
            Keywords = ["tr"],
            HotKey = "Alt+C"
        };

        var bus = new MessageBus();
        var manager = new PluginSessionManager(bus, new CapabilityGateway(), new NoopProcessFactory());
        return new NodePluginFactory(NullLoggerFactory.Instance, null, bus, manager)
            .CreatePlugins([manifest]).Single();
    }

    private sealed class NoopProcessFactory : INodeProcessControllerFactory
    {
        public INodeProcessController Create(string nodeExePath, string nodeEntryFullPath) => new NoopController();

        private sealed class NoopController : INodeProcessController
        {
            public IMessageTransport? Transport { get; } = new InMemoryTransport();
            public MyTools.Host.Core.Security.ProcessIdentity? ObservedIdentity { get; }

            public Task StartAsync(
                string pipeName, string pluginId, string entryId,
                Func<MyTools.Host.Core.Security.ProcessIdentity, string> issueToken,
                CancellationToken c) => Task.CompletedTask;

            public Task StopAsync() => Task.CompletedTask;
        }
    }
}
