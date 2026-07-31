using Microsoft.Extensions.DependencyInjection;
using MyTools.Common;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Plugins;

public static class PluginServiceCollectionExtensions
{
    public static IServiceCollection AddPluginServices(this IServiceCollection services)
    {
        services.AddSingleton<SearchHistoryDbHelper>();
        services.AddSingleton<NodePluginCatalog>();
        services.AddSingleton<NodePluginFactory>();
        
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
        services.AddSingleton<IPlugin, JsonFormatterPlugin>();
        services.AddSingleton<IPlugin, XmlFormatterPlugin>();
        services.AddSingleton<IPlugin, UuidGeneratorPlugin>();
        services.AddSingleton<IPlugin, DllInterfaceReaderPlugin>();
        services.AddSingleton<IPlugin, ChromeBookmarksPlugin>();
        
        services.AddSingleton<ClipBoardPlugin>();
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<ClipBoardPlugin>());
        services.AddSingleton<IWindowMessageHandler>(sp => sp.GetRequiredService<ClipBoardPlugin>());
        
        return services;
    }
}