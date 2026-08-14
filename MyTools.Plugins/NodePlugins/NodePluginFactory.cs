using Microsoft.Extensions.Logging;
using MyTools.Common.Localization;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Sessions;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginFactory
{
    private readonly ILoggerFactory loggerFactory;
    private readonly ILocalizationService localizationService;
    private readonly MessageBus? bus;
    private readonly PluginSessionManager? sessionManager;
    private readonly bool useV3Transport;

    public NodePluginFactory(ILoggerFactory loggerFactory)
        : this(loggerFactory, InvariantLocalizationService.Instance)
    {
    }

    public NodePluginFactory(ILoggerFactory loggerFactory, ILocalizationService localizationService)
        : this(loggerFactory, localizationService, bus: null, sessionManager: null, useV3Transport: false)
    {
    }

    public NodePluginFactory(ILoggerFactory loggerFactory, ILocalizationService? localizationService,
        MessageBus? bus, PluginSessionManager? sessionManager, bool useV3Transport)
    {
        this.loggerFactory = loggerFactory;
        this.localizationService = localizationService ?? new InvariantLocalizationService();
        this.bus = bus;
        this.sessionManager = sessionManager;
        this.useV3Transport = useV3Transport;
    }

    public IReadOnlyList<NodePlugin> CreatePlugins(IEnumerable<NodePluginManifest> manifests)
    {
        return manifests
            .Select(manifest =>
            {
                INodePluginHost host = CreateHost(manifest);
                return new NodePlugin(manifest, host, loggerFactory.CreateLogger<NodePlugin>(), localizationService);
            })
            .ToList();
    }

    private INodePluginHost CreateHost(NodePluginManifest manifest)
    {
        var isV3 = useV3Transport
                   && bus is not null
                   && sessionManager is not null
                   && manifest.ProtocolVersion == "3.0";

        var hostLogger = loggerFactory.CreateLogger<NodePluginFactory>();
        hostLogger.LogInformation("CreateHost: plugin={Id} proto={Proto} useV3={UseV3} bus={Bus} mgr={Mgr} -> isV3={IsV3}",
            manifest.Id, manifest.ProtocolVersion, useV3Transport, bus is not null, sessionManager is not null, isV3);

        if (isV3)
        {
            return new NodePluginBusHost(manifest, sessionManager!, bus!,
                loggerFactory.CreateLogger<NodePluginBusHost>());
        }

        return new NodePluginProcessHost(manifest, loggerFactory.CreateLogger<NodePluginProcessHost>());
    }

    /// <summary>Test-only accessor for the backend host backing a NodePlugin.</summary>
    internal INodePluginHost? GetHostForTest(NodePlugin plugin) => plugin.GetHostForTest();

    private sealed class InvariantLocalizationService : ILocalizationService
    {
        public static InvariantLocalizationService Instance { get; } = new();
        public string CurrentLocale => "en-US";

        public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null) =>
            LocalizedMessage.Format(defaultValue, LocalizedMessage.ToDictionary(values), System.Globalization.CultureInfo.InvariantCulture);

        public event EventHandler<LocaleChangedEventArgs>? LocaleChanged
        {
            add { }
            remove { }
        }
    }
}
