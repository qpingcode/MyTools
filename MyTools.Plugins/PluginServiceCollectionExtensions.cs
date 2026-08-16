using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Sessions;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Plugins;

public static class PluginServiceCollectionExtensions
{
    public static IServiceCollection AddPluginServices(this IServiceCollection services)
    {
        // Gateway must be the same instance MessageBus uses so RegisterManifest and Authorize share state.
        services.AddSingleton<CapabilityGateway>();
        services.AddSingleton(sp => new MessageBus(
            sp.GetRequiredService<CapabilityGateway>(),
            logger: sp.GetService<ILoggerFactory>()?.CreateLogger("MyTools.Host.Core.Bus.MessageBus")));
        services.AddSingleton<INodeProcessControllerFactory, Host.Transports.Process.NodeProcessControllerFactory>();
        services.AddSingleton(sp => new PluginSessionManager(
            sp.GetRequiredService<MessageBus>(),
            sp.GetRequiredService<CapabilityGateway>(),
            sp.GetRequiredService<INodeProcessControllerFactory>(),
            logger: sp.GetService<ILoggerFactory>()?.CreateLogger("MyTools.Host.Core.Sessions.PluginSessionManager")));

        services.AddSingleton<SearchHistoryDbHelper>();
        services.AddSingleton<NodePluginCatalog>();
        services.AddSingleton<NodePluginFactory>(sp =>
            new NodePluginFactory(
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetService<Common.Localization.ILocalizationService>(),
                sp.GetRequiredService<MessageBus>(),
                sp.GetRequiredService<PluginSessionManager>()));

        services.AddSingleton<PluginLoader>();
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<IKeywordRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddSingleton<IGlobalSearchRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddSingleton<IActionRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddSingleton<Searcher>();
        services.AddSingleton<ISearcher>(sp => sp.GetRequiredService<Searcher>());

        services.AddSingleton<IPlugin, FileSearcher>();
        services.AddSingleton<IPlugin, CommandRunner>();
        services.AddSingleton<IPlugin, SearchEnginePlugin>();
        services.AddSingleton<IPlugin, ProcessKillerPlugin>();
        services.AddSingleton<IPlugin, PluginSearcher>();
        services.AddSingleton<IPlugin, CalculatorPlugin>();
        services.AddSingleton<IPlugin, UuidGeneratorPlugin>();
        services.AddSingleton<IPlugin, DllInterfaceReaderPlugin>();
        services.AddSingleton<IPlugin, ChromeBookmarksPlugin>();

        services.AddSingleton<ClipBoardPlugin>();
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<ClipBoardPlugin>());
        services.AddSingleton<IWindowMessageHandler>(sp => sp.GetRequiredService<ClipBoardPlugin>());

        return services;
    }
}
