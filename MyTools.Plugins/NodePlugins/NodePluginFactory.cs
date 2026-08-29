using Microsoft.Extensions.Logging;
using MyTools.Common.Localization;
using MyTools.Common.Theming;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Sessions;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginFactory
{
    private readonly ILoggerFactory loggerFactory;
    private readonly ILocalizationService localizationService;
    private readonly IThemeService themeService;
    private readonly MessageBus bus;
    private readonly PluginSessionManager sessionManager;

    public NodePluginFactory(
        ILoggerFactory loggerFactory,
        ILocalizationService? localizationService,
        MessageBus bus,
        PluginSessionManager sessionManager,
        IThemeService? themeService = null)
    {
        this.loggerFactory = loggerFactory;
        this.localizationService = localizationService ?? new InvariantLocalizationService();
        this.themeService = themeService ?? FallbackThemeService.Instance;
        this.bus = bus;
        this.sessionManager = sessionManager;
    }

    public IReadOnlyList<NodePlugin> CreatePlugins(IEnumerable<NodePluginManifest> manifests)
    {
        var manifestList = manifests.ToList();
        var duplicateIds = manifestList
            .GroupBy(manifest => manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return manifestList
            .Select(manifest =>
            {
                INodePluginHost host = new NodePluginBusHost(
                    manifest, sessionManager, bus, loggerFactory.CreateLogger<NodePluginBusHost>())
                {
                    NodeExePath = NodeRuntimeLocator.Resolve()
                };
                var plugin = new NodePlugin(
                    manifest, host, loggerFactory.CreateLogger<NodePlugin>(),
                    localizationService, themeService);
                if (duplicateIds.Contains(manifest.Id))
                {
                    plugin.IsEnabled = false;
                }
                return plugin;
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

    private sealed class FallbackThemeService : IThemeService
    {
        public static FallbackThemeService Instance { get; } = new();
        public ThemeKind CurrentTheme => ThemeKind.Dark;
        public void SetTheme(ThemeKind theme) { }
        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged
        {
            add { }
            remove { }
        }
    }
}
