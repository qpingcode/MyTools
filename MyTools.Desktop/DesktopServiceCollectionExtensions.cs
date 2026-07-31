using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Utils;
using MyTools.Common.WindowsMessageHandler;
using MyTools.Desktop.Components;
using MyTools.Desktop.Models;
using MyTools.Desktop.Services;
using MyTools.Desktop.Services.WindowNativeHandler;
using MyTools.Desktop.Storage;
using MyTools.Desktop.Utils;
using MyTools.Desktop.ViewModels;
using MyTools.Desktop.Views;
using MyTools.Plugins.NodePlugins;
using Serilog;
using Serilog.Events;

namespace MyTools.Desktop;

public static class DesktopServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<MessageOnlyWindow>();
        
        // windows
        serviceCollection.AddTransient<SearchWindow>();  // 如果是 singleton的，每次打开都会闪烁
        serviceCollection.AddTransient<SearchViewModel>();
        
        // services
        serviceCollection.AddSingleton<AppConfigService>();
        serviceCollection.AddSingleton<AutoStartService>();
        serviceCollection.AddSingleton<IUpdateService, UpdateService>();
        serviceCollection.AddSingleton<LanguageService>();
        serviceCollection.AddSingleton<GestureRegistry>();
        serviceCollection.AddSingleton<MouseHelper>();
        serviceCollection.AddSingleton<NodePluginDetailNavigator>();
        serviceCollection.AddSingleton<INodePluginDetailNavigator>(sp => sp.GetRequiredService<NodePluginDetailNavigator>());
        serviceCollection.AddSingleton<HotKeyManager>();
        serviceCollection.AddSingleton<HotKeyMessageHandler>();
        serviceCollection.AddSingleton<IGlobal, Global>();
        serviceCollection.AddSingleton<IKeyboardHelper, KeyboardHelperForPlugin>();
        
        // Windows Message Handlers
        serviceCollection.AddSingleton<MessageOnlyWindow>();
        serviceCollection.AddSingleton<IWindowMessageHandler>(sp => sp.GetRequiredService<HotKeyMessageHandler>());
        serviceCollection.AddSingleton<IWindowHandleAware>(sp => sp.GetRequiredService<HotKeyMessageHandler>());
        
        return serviceCollection;
    }
    
    public static IServiceCollection AddLog(this IServiceCollection serviceCollection)
    {
        var logPath = Path.Join(ConfigPath.Base, "logs/log.txt");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, restrictedToMinimumLevel: LogEventLevel.Information)
            .CreateLogger();
        
        
        serviceCollection.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog();
        });

        return serviceCollection;
    }
    
    public static IServiceCollection AddConfigurationSystem(this IServiceCollection services)
    {
        services.AddSingleton<IConfigurationStorage, JsonConfigurationStorage>();
        services.AddSingleton<IConfigurationRegistry, ConfigurationRegistry>();
       
        
        return services;
    }
}