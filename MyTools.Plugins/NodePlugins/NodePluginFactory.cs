using Microsoft.Extensions.Logging;
using MyTools.Common.Localization;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginFactory
{
    private readonly ILoggerFactory loggerFactory;
    private readonly ILocalizationService localizationService;

    public NodePluginFactory(ILoggerFactory loggerFactory)
        : this(loggerFactory, InvariantLocalizationService.Instance)
    {
    }

    public NodePluginFactory(ILoggerFactory loggerFactory, ILocalizationService localizationService)
    {
        this.loggerFactory = loggerFactory;
        this.localizationService = localizationService;
    }

    public IReadOnlyList<NodePlugin> CreatePlugins(IEnumerable<NodePluginManifest> manifests)
    {
        return manifests
            .Select(manifest =>
            {
                var processHost = new NodePluginProcessHost(manifest, loggerFactory.CreateLogger<NodePluginProcessHost>());
                return new NodePlugin(manifest, processHost, loggerFactory.CreateLogger<NodePlugin>(), localizationService);
            })
            .ToList();
    }

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