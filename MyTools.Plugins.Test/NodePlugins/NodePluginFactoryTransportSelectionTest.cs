using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Common.Localization;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

/// <summary>
/// Verifies the factory selects the backend host by protocol version: v3 manifests get the bus
/// host, v2 manifests get the legacy stdio host. The selection is observable via the NodePlugin's
/// backend host type (exposed for tests).
/// </summary>
[TestFixture]
public class NodePluginFactoryTransportSelectionTest
{
    private static NodePluginManifest V2Manifest() => new()
    {
        Id = "p:main", ParentId = "p", EntryId = "main", ProtocolVersion = "2.0",
        Entry = "index.mjs", PluginDirectory = "C:/p", EntryFullPath = "C:/p/index.mjs",
    };

    private static NodePluginManifest V3Manifest() => new()
    {
        Id = "p:main", ParentId = "p", EntryId = "main", ProtocolVersion = "3.0",
        Entry = "index.mjs", PluginDirectory = "C:/p", EntryFullPath = "C:/p/index.mjs",
    };

    private static NodePluginFactory CreateFactory()
    {
        var bus = new MessageBus();
        var manager = new PluginSessionManager(bus, new CapabilityGateway(), new FakeFactory());
        return new NodePluginFactory(NullLoggerFactory.Instance,
            NullLocalizationService.Instance, bus, manager, useV3Transport: true);
    }

    [Test]
    public void CreatePlugins_V3Manifest_WhenV3Enabled_ShouldUseBusHost()
    {
        var factory = CreateFactory();
        var plugins = factory.CreatePlugins([V3Manifest()]);
        var host = factory.GetHostForTest(plugins[0]);

        Assert.That(host, Is.Not.Null);
        Assert.That(host!.GetType().Name, Is.EqualTo("NodePluginBusHost"));
    }

    [Test]
    public void CreatePlugins_V2Manifest_WhenV3Enabled_ShouldUseStdioHost()
    {
        var factory = CreateFactory();
        var plugins = factory.CreatePlugins([V2Manifest()]);
        var host = factory.GetHostForTest(plugins[0]);

        Assert.That(host!.GetType().Name, Is.EqualTo("NodePluginProcessHost"));
    }

    [Test]
    public void CreatePlugins_WhenV3Disabled_ShouldAlwaysUseStdioHost()
    {
        var bus = new MessageBus();
        var manager = new PluginSessionManager(bus, new CapabilityGateway(), new FakeFactory());
        var factory = new NodePluginFactory(NullLoggerFactory.Instance,
            NullLocalizationService.Instance, bus, manager, useV3Transport: false);

        var plugins = factory.CreatePlugins([V3Manifest()]);
        var host = factory.GetHostForTest(plugins[0]);

        Assert.That(host!.GetType().Name, Is.EqualTo("NodePluginProcessHost"));
    }

    private sealed class NullLocalizationService : MyTools.Common.Localization.ILocalizationService
    {
        public static NullLocalizationService Instance { get; } = new();
        public string CurrentLocale => "en-US";
        public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null) => defaultValue;
        public event EventHandler<MyTools.Common.Localization.LocaleChangedEventArgs>? LocaleChanged { add { } remove { } }
    }

    private sealed class FakeFactory : INodeProcessControllerFactory
    {
        public INodeProcessController Create(string nodeExePath, string nodeEntryFullPath) => new FakeController();

        private sealed class FakeController : INodeProcessController
        {
            public IMessageTransport? Transport { get; private set; }
            public MyTools.Host.Core.Security.ProcessIdentity? ObservedIdentity { get; private set; }

            public Task StartAsync(
                string pipeName,
                string pluginId,
                string entryId,
                Func<MyTools.Host.Core.Security.ProcessIdentity, string> issueToken,
                CancellationToken c)
            {
                Transport = new Host.Core.Transports.InMemoryTransport();
                ObservedIdentity = new MyTools.Host.Core.Security.ProcessIdentity(
                    1, DateTime.UtcNow, pluginId, entryId);
                issueToken(ObservedIdentity);
                return Task.CompletedTask;
            }

            public Task StopAsync() => Task.CompletedTask;
        }
    }
}
