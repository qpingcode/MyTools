using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Localization;
using MyTools.Common.Theming;
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
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using Serilog;

namespace MyTools.Desktop;

public static class DesktopServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<MouseGestureDetector>();
        services.AddPluginServices();
        services.AddDesktopServices();
        services.AddConfigurationSystem();
        services.AddLog();
        services.AddMemoryCache();
        services.AddSingleton<AppBootstrapper>();

        return services;
    }

    public static IServiceCollection AddDesktopServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<MessageOnlyWindow>();
        
        // windows
        serviceCollection.AddTransient<SearchWindow>();  // 如果是 singleton的，每次打开都会闪烁
        serviceCollection.AddTransient<SearchViewModel>();
        serviceCollection.AddTransient<PluginWindow>();
        serviceCollection.AddTransient<PluginViewModel>();
        serviceCollection.AddSingleton<PluginWindowManager>();
        
        // services
        serviceCollection.AddSingleton<AppConfigService>();
        serviceCollection.AddSingleton<AutoStartService>();
        serviceCollection.AddSingleton<IUpdateService, UpdateService>();
        serviceCollection.AddSingleton<LanguageService>();
        serviceCollection.AddSingleton<ILocalizationService>(sp => sp.GetRequiredService<LanguageService>());
        serviceCollection.AddSingleton<ThemeService>();
        serviceCollection.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
        serviceCollection.AddSingleton<GestureRegistry>();
        serviceCollection.AddSingleton<MouseHelper>();
        serviceCollection.AddSingleton<NodePluginDetailNavigator>();
        serviceCollection.AddSingleton<INodePluginDetailNavigator>(sp => sp.GetRequiredService<NodePluginDetailNavigator>());
        serviceCollection.AddSingleton<GlobalExceptionHandler>();
        serviceCollection.AddSingleton<SettingsPluginHostCallHandler>();
        serviceCollection.AddSingleton<KeymapOverrideProvider>();
        serviceCollection.AddSingleton<KeymapService>();
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
        // The level switch lets the minimum log level change at runtime (via LogLevelService)
        // without rebuilding the logger or restarting the app.
        var logLevelService = new LogLevelService();
        var logPath = Path.Join(ConfigPath.Base, "logs/log.txt");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(logLevelService.LevelSwitch)
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        serviceCollection.AddSingleton(logLevelService);

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