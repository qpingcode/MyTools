using Microsoft.Extensions.Logging;
using MyTools.Common.Localization;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Sessions;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginFactory
{
    private readonly ILoggerFactory loggerFactory;
    private readonly ILocalizationService localizationService;
    private readonly MessageBus bus;
    private readonly PluginSessionManager sessionManager;

    public NodePluginFactory(
        ILoggerFactory loggerFactory,
        ILocalizationService? localizationService,
        MessageBus bus,
        PluginSessionManager sessionManager)
    {
        this.loggerFactory = loggerFactory;
        this.localizationService = localizationService ?? new InvariantLocalizationService();
        this.bus = bus;
        this.sessionManager = sessionManager;
    }

    public IReadOnlyList<NodePlugin> CreatePlugins(IEnumerable<NodePluginManifest> manifests)
    {
        return manifests
            .Select(manifest =>
            {
                INodePluginHost host = new NodePluginBusHost(
                    manifest, sessionManager, bus, loggerFactory.CreateLogger<NodePluginBusHost>());
                return new NodePlugin(manifest, host, loggerFactory.CreateLogger<NodePlugin>(), localizationService);
            })
            .ToList();
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
