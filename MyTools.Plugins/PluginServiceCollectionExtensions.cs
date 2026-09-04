using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Sessions;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Plugins;

public static class PluginServiceCollectionExtensions
{
    public static IServiceCollection AddPluginServices(this IServiceCollection services)
    {
        // Gateway must be the same instance MessageBus uses so RegisterManifest and Authorize share state.
        services.AddSingleton<CapabilityGateway>();
        services.AddSingleton<IPluginDiagnosticsService, PluginDiagnosticsService>();
        services.AddSingleton(sp => new MessageBus(
            sp.GetRequiredService<CapabilityGateway>(),
            diagnostics: sp.GetRequiredService<IPluginDiagnosticsService>(),
            logger: sp.GetService<ILoggerFactory>()?.CreateLogger("MyTools.Host.Core.Bus.MessageBus")));
        services.AddSingleton<INodeProcessControllerFactory>(_ =>
            new Host.Transports.Process.NodeProcessControllerFactory(ConfigPath.PluginsDataPath));
        services.AddSingleton(sp => new PluginSessionManager(
            sp.GetRequiredService<MessageBus>(),
            sp.GetRequiredService<CapabilityGateway>(),
            sp.GetRequiredService<INodeProcessControllerFactory>(),
            diagnostics: sp.GetRequiredService<IPluginDiagnosticsService>(),
            logger: sp.GetService<ILoggerFactory>()?.CreateLogger("MyTools.Host.Core.Sessions.PluginSessionManager")));

        services.AddSingleton<SearchHistoryDbHelper>();
        services.AddSingleton<NodePluginCatalog>();
        services.AddSingleton<NodePluginFactory>(sp =>
            new NodePluginFactory(
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetService<Common.Localization.ILocalizationService>(),
                sp.GetRequiredService<MessageBus>(),
                sp.GetRequiredService<PluginSessionManager>(),
                sp.GetRequiredService<IPluginDiagnosticsService>(),
                sp.GetService<Common.Theming.IThemeService>()));

        services.AddSingleton<PluginLoader>();
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<IKeywordRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddSingleton<IGlobalSearchRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddSingleton<IActionRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddSingleton<Searcher>();
        services.AddSingleton<ISearcher>(sp => sp.GetRequiredService<Searcher>());

        services.AddSingleton<IPlugin, FileSearcher>();

        services.AddSingleton<ClipBoardPlugin>();
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<ClipBoardPlugin>());
        services.AddSingleton<IWindowMessageHandler>(sp => sp.GetRequiredService<ClipBoardPlugin>());

        return services;
    }
}
