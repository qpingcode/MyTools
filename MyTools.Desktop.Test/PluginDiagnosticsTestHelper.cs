using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MyTools.Common;
using MyTools.Common.Localization;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Desktop.Test;

internal static class PluginDiagnosticsTestHelper
{
    public static NodePlugin CreateNodePlugin(string pluginId, IPluginDiagnosticsService diagnostics)
    {
        var bus = new MessageBus();
        var sessionManager = new PluginSessionManager(bus, new CapabilityGateway(), new FakeProcessControllerFactory());
        var factory = new NodePluginFactory(
            NullLoggerFactory.Instance,
            TestLocalizationService.Instance,
            bus,
            sessionManager,
            diagnostics);

        return factory.CreatePlugins(
        [
            new NodePluginManifest
            {
                Id = pluginId,
                Version = "1.0.0",
                Runtime = "node",
                ProtocolVersion = "3.0",
                Entry = "index.mjs",
                PluginDirectory = $@"C:\plugins\{pluginId}",
                EntryFullPath = $@"C:\plugins\{pluginId}\index.mjs"
            }
        ]).Single();
    }

    public static PluginLoader CreatePluginLoader(NodePlugin nodePlugin, IPluginDiagnosticsService diagnostics)
    {
        var keywordRegistry = new Mock<IKeywordRegistry>(MockBehavior.Strict);
        var globalSearchRegistry = new Mock<IGlobalSearchRegistry>(MockBehavior.Strict);
        globalSearchRegistry.SetupGet(registry => registry.Plugins).Returns(Array.Empty<IPlugin>());
        var actionRegistry = new Mock<IActionRegistry>(MockBehavior.Strict);
        var pluginHotKeyRegistry = new Mock<IPluginHotKeyRegistry>(MockBehavior.Strict);
        var searcher = new Searcher(
            globalSearchRegistry.Object,
            new MemoryCache(new MemoryCacheOptions()),
            new SearchHistoryDbHelper(Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "plugin-diagnostics-tests",
                $"{Guid.NewGuid():N}.db")),
            NullLogger<Searcher>.Instance);
        var pluginLoader = new PluginLoader(
            NullLogger<PluginLoader>.Instance,
            keywordRegistry.Object,
            globalSearchRegistry.Object,
            actionRegistry.Object,
            pluginHotKeyRegistry.Object,
            [],
            searcher,
            new NodePluginCatalog(
                Path.Combine(TestContext.CurrentContext.WorkDirectory, "plugin-diagnostics-plugin-root"),
                NullLogger<NodePluginCatalog>.Instance),
            CreateNodePluginFactory(diagnostics));

        var dynamicPluginsField = typeof(PluginLoader).GetField("dynamicPlugins", BindingFlags.Instance | BindingFlags.NonPublic);
        var dynamicPlugins = (List<IPlugin>)dynamicPluginsField!.GetValue(pluginLoader)!;
        dynamicPlugins.Add(nodePlugin);
        return pluginLoader;
    }

    public static TestLocalizationService Localization => TestLocalizationService.Instance;

    private static NodePluginFactory CreateNodePluginFactory(IPluginDiagnosticsService diagnostics)
    {
        var bus = new MessageBus();
        return new NodePluginFactory(
            NullLoggerFactory.Instance,
            TestLocalizationService.Instance,
            bus,
            new PluginSessionManager(bus, new CapabilityGateway(), new FakeProcessControllerFactory()),
            diagnostics);
    }

    private sealed class FakeProcessControllerFactory : INodeProcessControllerFactory
    {
        public INodeProcessController Create(string nodeExePath, string nodeEntryFullPath) => new FakeProcessController();
    }

    private sealed class FakeProcessController : INodeProcessController
    {
        public IMessageTransport? Transport { get; private set; }
        public ProcessIdentity? ObservedIdentity { get; private set; }

        public event Action<NodeProcessExitInfo>? ProcessExited
        {
            add { }
            remove { }
        }

        public Task StartAsync(
            string pipeName,
            string pluginId,
            Func<ProcessIdentity, string> issueToken,
            CancellationToken cancellationToken)
        {
            Transport = new InMemoryTransport();
            ObservedIdentity = new ProcessIdentity(1, DateTime.UtcNow, pluginId);
            issueToken(ObservedIdentity);
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;
    }
}

internal sealed class StaticPluginDiagnosticsService(PluginDiagnosticsSnapshot snapshot) : IPluginDiagnosticsService
{
    public PluginDiagnosticsSnapshot Snapshot { get; set; } = snapshot;

    public PluginDiagnosticsSnapshot GetSnapshot() => Snapshot;
    public void RecordDiagnostic(Microsoft.Extensions.Logging.LogLevel level, string category, string message, string? pluginId = null, string? sessionId = null, string? endpointId = null, string? route = null, string? correlationId = null, string? details = null) { }
    public void RecordSessionState(string pluginId, string sessionId, SessionState state, int? pid = null, string? failureDetails = null) { }
    public void ClearSession(string pluginId, string sessionId, SessionState state, string? failureDetails = null) { }
    public void AttachProcessController(string pluginId, string sessionId, INodeProcessController controller) { }
    public void DetachProcessController(string pluginId, string sessionId, INodeProcessController controller) { }
    public void RecordDisconnect(string pluginId, string sessionId, bool willRestart, string? failureDetails = null) { }
    public void RecordRestart(string pluginId, string previousSessionId, string currentSessionId) { }
    public void RecordRestartExhausted(string pluginId, string sessionId, string? failureDetails = null) { }
    public void RecordHeartbeatTimeout(string pluginId, string sessionId, int consecutiveTimeouts, bool nowDead) { }
    public void RecordProcessExit(string pluginId, string sessionId, NodeProcessExitInfo exitInfo) { }
    public void RecordCallCompleted(string pluginId, string sessionId, string endpointId, string route, string correlationId, double elapsedMs, PluginCallOutcome outcome, string? details = null) { }
    public void RecordCallTimeout(string pluginId, string sessionId, string endpointId, string route, string correlationId, double elapsedMs, string? details = null) { }
    public void RecordCallRejected(string pluginId, string sessionId, string endpointId, string route, string correlationId, string reason) { }
    public void UpdateEndpointPending(string pluginId, string sessionId, string endpointId, int inFlight, int limit, int highWaterMark) { }
    public void RemoveEndpoint(string pluginId, string sessionId, string endpointId) { }
    public void UpdateEventQueueState(string pluginId, string sessionId, string endpointId, int depth, int capacity, int highWaterMark, long droppedTotal, double oldestWaitMs) { }
    public void RecordEventQueued(string pluginId, string sessionId, string endpointId, string route, int depth, int capacity, int highWaterMark, long droppedTotal, bool dropped, double oldestWaitMs, string? droppedRoute = null) { }
    public void RecordEventDelivered(string pluginId, string sessionId, string endpointId, string route, double queueWaitMs, double deliveryMs, int depth, int capacity, int highWaterMark, long droppedTotal, double oldestWaitMs) { }
}

internal sealed class TestLocalizationService : ILocalizationService
{
    public static TestLocalizationService Instance { get; } = new();

    public string CurrentLocale => "en-US";

    public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null)
        => defaultValue;

    public event EventHandler<LocaleChangedEventArgs>? LocaleChanged
    {
        add { }
        remove { }
    }
}
