using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Common.Localization;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class NodePluginFactoryTransportSelectionTest
{
    private static NodePluginManifest Manifest() => new()
    {
        Id = "p", ProtocolVersion = "3.0",
        Entry = "index.mjs", PluginDirectory = "C:/p", EntryFullPath = "C:/p/index.mjs",
    };

    [Test]
    public void CreatePlugins_ShouldUseBusHost()
    {
        var bus = new MessageBus();
        var manager = new PluginSessionManager(bus, new CapabilityGateway(), new FakeFactory());
        var factory = new NodePluginFactory(
            NullLoggerFactory.Instance,
            NullLocalizationService.Instance,
            bus,
            manager,
            new PluginDiagnosticsService());

        var plugins = factory.CreatePlugins([Manifest()]);
        var host = factory.GetHostForTest(plugins[0]);

        Assert.That(host, Is.Not.Null);
        Assert.That(host!.GetType().Name, Is.EqualTo("NodePluginBusHost"));
    }

    private sealed class NullLocalizationService : ILocalizationService
    {
        public static NullLocalizationService Instance { get; } = new();
        public string CurrentLocale => "en-US";
        public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null) => defaultValue;
        public event EventHandler<LocaleChangedEventArgs>? LocaleChanged { add { } remove { } }
    }

    private sealed class FakeFactory : INodeProcessControllerFactory
    {
        public INodeProcessController Create(string nodeExePath, string nodeEntryFullPath) => new FakeController();

        private sealed class FakeController : INodeProcessController
        {
            public IMessageTransport? Transport { get; private set; }
            public MyTools.Host.Core.Security.ProcessIdentity? ObservedIdentity { get; private set; }
            public event Action<NodeProcessExitInfo>? ProcessExited
            {
                add { }
                remove { }
            }

            public Task StartAsync(
                string pipeName,
                string pluginId,
                Func<MyTools.Host.Core.Security.ProcessIdentity, string> issueToken,
                CancellationToken c)
            {
                Transport = new InMemoryTransport();
                ObservedIdentity = new MyTools.Host.Core.Security.ProcessIdentity(
                    1, DateTime.UtcNow, pluginId);
                issueToken(ObservedIdentity);
                return Task.CompletedTask;
            }

            public Task StopAsync() => Task.CompletedTask;

            public NodeProcessResourceUsage? TryGetResourceUsage() => null;
        }
    }
}
