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
using MyTools.AI;
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
    internal const string ConsoleLogOutputTemplate =
        "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";
    internal const string FileLogOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

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
        serviceCollection.AddSingleton<NativeMessageWindowHost>();
        
        // windows
        serviceCollection.AddTransient<SearchWindow>();  // 如果是 singleton的，每次打开都会闪烁
        serviceCollection.AddTransient<SearchViewModel>();
        serviceCollection.AddTransient<PluginWindow>();
        serviceCollection.AddTransient<PluginViewModel>();
        serviceCollection.AddSingleton<PluginWindowManager>();
        serviceCollection.AddSingleton<PluginLauncher>();
        serviceCollection.AddSingleton<IPluginLauncher>(sp => sp.GetRequiredService<PluginLauncher>());
        serviceCollection.AddSingleton<WindowPlacementService>();
        
        // services
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
        serviceCollection.AddSingleton<IPluginHostCapabilityHandler>(sp =>
            sp.GetRequiredService<SettingsPluginHostCallHandler>());
        serviceCollection.AddSingleton<PathPluginHostCallHandler>();
        serviceCollection.AddSingleton<IPluginHostCapabilityHandler>(sp =>
            sp.GetRequiredService<PathPluginHostCallHandler>());
        serviceCollection.AddSingleton<RestartPluginHostCallHandler>();
        serviceCollection.AddSingleton<IPluginHostCapabilityHandler>(sp =>
            sp.GetRequiredService<RestartPluginHostCallHandler>());
        serviceCollection.AddSingleton<DevelopmentPluginService>();
        serviceCollection.AddSingleton<IPluginCreationProxyProvider, PluginCreationProxyProvider>();
        serviceCollection.AddSingleton<HostCityService>();
        serviceCollection.AddSingleton<IHostCityProvider>(sp => sp.GetRequiredService<HostCityService>());
        serviceCollection.AddSingleton<LocationPluginHostCallHandler>();
        serviceCollection.AddSingleton<IPluginHostCapabilityHandler>(sp =>
            sp.GetRequiredService<LocationPluginHostCallHandler>());
        serviceCollection.AddSingleton(sp =>
        {
            var developmentPlugins = sp.GetRequiredService<DevelopmentPluginService>();
            var repositoryRoot = FindRepositoryRoot();
            var examplesRoot = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Examples"))
                ? Path.Combine(AppContext.BaseDirectory, "Examples")
                : Path.Combine(repositoryRoot, "MyTools.Plugins", "Examples");
            var deployedSkill = Path.Combine(AppContext.BaseDirectory, "skills", "create-plugin", "SKILL.md");
            var skillPath = File.Exists(deployedSkill)
                ? deployedSkill
                : Path.Combine(repositoryRoot, ".github", "skills", "create-plugin", "SKILL.md");
            var existing = developmentPlugins.GetKnownPlugins()
                .Select(plugin => new ExistingPlugin(plugin.Id, plugin.Name))
                .ToArray();
            return new PluginCreationAgentService(new PluginCreationContext(
                repositoryRoot,
                examplesRoot,
                Path.Combine(ConfigPath.Base, "plugins"),
                developmentPlugins.CodingRoot,
                ConfigPath.Base,
                skillPath,
                existing,
                sp.GetRequiredService<IHostCityProvider>(),
                Path.Combine(Path.GetDirectoryName(skillPath)!, "references"),
                developmentPlugins,
                sp.GetRequiredService<IPluginCreationProxyProvider>()),
                sp.GetRequiredService<ILogger<PluginCreationAgentService>>());
        });
        serviceCollection.AddSingleton<DevelopmentPluginHostCallHandler>();
        serviceCollection.AddSingleton<IPluginHostCapabilityHandler>(sp =>
            sp.GetRequiredService<DevelopmentPluginHostCallHandler>());
        serviceCollection.AddSingleton<NodePluginHostCallRouter>();
        serviceCollection.AddSingleton<InputActionCaptureService>();
        serviceCollection.AddSingleton<PluginOverrideProvider>();
        serviceCollection.AddSingleton<GestureConfigProvider>();
        serviceCollection.AddSingleton<PluginHotKeyService>();
        serviceCollection.AddSingleton<PluginKeymapService>();
        serviceCollection.AddSingleton<HotKeyManager>();
        serviceCollection.AddSingleton<HotKeyMessageHandler>();
        serviceCollection.AddSingleton<IGlobal, Global>();
        serviceCollection.AddSingleton<IKeyboardHelper, KeyboardHelperForPlugin>();
        
        // Windows Message Handlers
        serviceCollection.AddSingleton<NativeMessageWindowHost>();
        serviceCollection.AddSingleton<IWindowMessageHandler>(sp => sp.GetRequiredService<HotKeyMessageHandler>());
        serviceCollection.AddSingleton<IWindowHandleAware>(sp => sp.GetRequiredService<HotKeyMessageHandler>());
        
        return serviceCollection;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, ".github", "skills", "create-plugin", "SKILL.md")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        return AppContext.BaseDirectory;
    }
    
    public static IServiceCollection AddLog(this IServiceCollection serviceCollection)
    {
        // The level switch lets the minimum log level change at runtime (via LogLevelService)
        // without rebuilding the logger or restarting the app.
        var logLevelService = new LogLevelService();
        var logPath = Path.Join(ConfigPath.Base, "logs/log.txt");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(logLevelService.LevelSwitch)
            .WriteTo.Console(outputTemplate: ConsoleLogOutputTemplate)
            .WriteTo.File(
                logPath,
                outputTemplate: FileLogOutputTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
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
