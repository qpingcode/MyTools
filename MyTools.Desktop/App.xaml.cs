using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Hardcodet.Wpf.TaskbarNotification.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Theming;
using MyTools.Desktop.Services;
using MyTools.Desktop.Utils;
using MyTools.Desktop.Views;
using MyTools.Plugins;
using MyTools.Plugins.Param;
using static MyTools.Desktop.Services.LanguageService;
using Icon = System.Drawing.Icon;

namespace MyTools.Desktop;

public partial class App
{
    private const string appName = "MyTools.Desktop";
    private static Mutex? _mutex;
    private bool ownsMutex;
    private TaskbarIcon? _notifyIcon;
    private AppBootstrapper? appBootstrapper;
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, appName, out var createdNew);
        ownsMutex = createdNew;
        if (!createdNew)
        {
            ProtocolActivationService.TrySendToRunningInstance(e.Args);
            Console.WriteLine("Another instance is running, shutting down.");
            Current.Shutdown();
            return;
        }

        var services = new ServiceCollection();
        services.AddApplicationServices();
        serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        ServiceLocator.ServiceProvider = serviceProvider;

        // 注册全局异常钩子（Dispatcher / AppDomain / UnobservedTask），
        // 统一记录日志并弹出 ErrorDialog 显示完整堆栈。
        var globalExceptionHandler = serviceProvider.GetRequiredService<GlobalExceptionHandler>();
        globalExceptionHandler.Register();

        InitializeNotifyIcon();
        serviceProvider.GetRequiredService<PluginDiagnosticsAlertService>();

        appBootstrapper = serviceProvider.GetRequiredService<AppBootstrapper>();
        var protocolActivation = serviceProvider.GetRequiredService<ProtocolActivationService>();
        protocolActivation.RegisterUriScheme();
        protocolActivation.StartListening();
        appBootstrapper.Init();
        protocolActivation.HandleStartup(e.Args);

        // Keep the tray menu and the General.Theme setting in sync with theme changes,
        // regardless of whether the change originated from the tray or the settings UI.
        var themeService = ServiceLocator.GetRequiredService<IThemeService>();
        themeService.ThemeChanged += OnThemeChanged;

        base.OnStartup(e);
    }

    private static void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        // Refresh the tray menu checkmarks.
        if (Current is App app)
        {
            app.UpdateNotifyIconMenu();
        }

        // Mirror the new theme into the General.Theme setting so the settings window
        // shows the current selection (the setting is otherwise only written on Save).
        var registry = ServiceLocator.GetRequiredService<IConfigurationRegistry>();
        registry.FindSetting(ThemeService.ThemeSettingPath)?
            .InitValueWithoutNotify(e.CurrentTheme.ToWireString());
    }

    private void InitializeNotifyIcon()
    {
        Icon? customIcon = null;
        string iconPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
            "Assets",
            "Maintenance.ico");

        if (File.Exists(iconPath))
        {
            customIcon = new Icon(iconPath);
        }
        
        _notifyIcon = new TaskbarIcon
        {
            Icon = customIcon ?? Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
            ToolTipText = GetCaption("AppTitleDetail", "My Tools")
        };
        
        EnsureDpiFactorsLoadSuccessfully();    

        _notifyIcon.ContextMenu = new ContextMenu();
        if (TryFindResource("UiFontFamily") is FontFamily uiFont)
        {
            _notifyIcon.ContextMenu.FontFamily = uiFont;
        }
        serviceProvider?.GetRequiredService<ITrayNotificationService>().Attach(_notifyIcon);
        UpdateNotifyIconMenu();
        
        _notifyIcon.TrayMouseDoubleClick += (_, _) => WindowHelper.ShowSearchWindow();
        _notifyIcon.ShowTrayPopup();
    }

    private void EnsureDpiFactorsLoadSuccessfully()
    {
        // make sure the DpiFactorX and DpiFactorY are loaded successfully
        // Otherwise, the contextMenu of the NotifyIcon will be in the wrong position the first time right-click
        _ = SystemInfo.DpiFactorX;
    }

    private void UpdateNotifyIconMenu()
    {
        if (_notifyIcon?.ContextMenu == null)
            return;

        _notifyIcon.ContextMenu.Items.Clear();

        var autoStartService = ServiceLocator.GetRequiredService<AutoStartService>();
        var languageService = ServiceLocator.GetRequiredService<LanguageService>();

        var openConfigItem = new MenuItem();
        openConfigItem.Header = GetCaption("OpenConfigFolder", "Open Config Folder");
        openConfigItem.Click += OpenConfigFolder_Click;
        _notifyIcon.ContextMenu.Items.Add(openConfigItem);

        var settingsItem = new MenuItem();
        settingsItem.Header = GetCaption("Settings", "Settings");
        settingsItem.Click += OpenSettings_Click;
        _notifyIcon.ContextMenu.Items.Add(settingsItem);

        var diagnosticsItem = new MenuItem();
        diagnosticsItem.Header = GetCaption("PluginDiagnostics.Menu", "Diagnostics");
        diagnosticsItem.Click += OpenPluginDiagnostics_Click;
        _notifyIcon.ContextMenu.Items.Add(diagnosticsItem);

        var updateService = ServiceLocator.GetRequiredService<IUpdateService>();
        var versionItem = new MenuItem
        {
            Header = GetCaption("CurrentVersion", "Version: {0}", updateService.CurrentVersion),
            IsEnabled = false
        };
        _notifyIcon.ContextMenu.Items.Add(versionItem);

        var checkUpdateItem = new MenuItem { Header = GetCaption("CheckForUpdates", "Check for Updates") };
        checkUpdateItem.Click += CheckForUpdates_Click;
        _notifyIcon.ContextMenu.Items.Add(checkUpdateItem);

        var autoStartItem = new MenuItem();
        autoStartItem.Header = GetCaption("AutoStart", "Auto Start");
        autoStartItem.IsChecked = autoStartService.AutoStart;
        autoStartItem.Click += AutoStart_Click;
        _notifyIcon.ContextMenu.Items.Add(autoStartItem);

        var languageMenu = new MenuItem();
        languageMenu.Header = GetCaption("Language", "Language");
        foreach (var culture in languageService.SupportedCultures)
        {
            var cultureItem = new MenuItem();
            cultureItem.Header = LanguageService.GetNativeDisplayName(culture);
            cultureItem.IsChecked = languageService.CurrentCulture.Name == culture.Name;
            cultureItem.Click += ChangeLanguage_Click;
            cultureItem.Tag = culture.Name;
            languageMenu.Items.Add(cultureItem);
        }
        
        _notifyIcon.ContextMenu.Items.Add(languageMenu);

        var themeService = ServiceLocator.GetRequiredService<IThemeService>();
        var themeMenu = new MenuItem();
        themeMenu.Header = GetCaption("Theme", "Theme");
        foreach (var theme in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            var themeItem = new MenuItem();
            themeItem.Header = GetCaption($"Theme.{theme.ToWireString()}", theme.ToString());
            themeItem.IsChecked = themeService.CurrentTheme == theme;
            themeItem.Click += ChangeTheme_Click;
            themeItem.Tag = theme;
            themeMenu.Items.Add(themeItem);
        }
        _notifyIcon.ContextMenu.Items.Add(themeMenu);

        var exitItem = new MenuItem { Header = GetCaption("Exit", "Exit") };
        exitItem.Click += (_, _) => Current.Shutdown();
        _notifyIcon.ContextMenu.Items.Add(exitItem);
    }

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        var updateService = ServiceLocator.GetRequiredService<IUpdateService>();
        var window = new UpdateCheckWindow(updateService);
        window.Show();
    }
    
    private void OpenConfigFolder_Click(object? sender, EventArgs e)
    {
        var param = ActionStringParam.From(Path.Combine(ConfigPath.Base, "Settings.json"));
        new OpenInExplorer().ExecuteAsync(param);
    }

    private void OpenSettings_Click(object? sender, EventArgs e)
    {
        try
        {
            appBootstrapper?.OpenSettings();
        }
        catch (Exception ex)
        {
            var logger = ServiceLocator.GetRequiredService<ILogger<App>>();
            logger.LogError(ex, "Failed to open settings window");
            MessageBox.Show(
                GetCaption("OpenSettingsError", "Failed to open settings: {0}", ex.Message),
                GetCaption("Error", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenPluginDiagnostics_Click(object? sender, EventArgs e)
    {
        try
        {
            ServiceLocator.GetRequiredService<PluginDiagnosticsWindowManager>().Show();
        }
        catch (Exception ex)
        {
            var logger = ServiceLocator.GetRequiredService<ILogger<App>>();
            logger.LogError(ex, "Failed to open plugin diagnostics window");
        }
    }
    
    private void AutoStart_Click(object? sender, EventArgs e)
    {
        var item = sender as MenuItem;
        if (item == null)
            return;

        item.IsChecked  = !item.IsChecked ;
        
        var autoStartService = ServiceLocator.GetRequiredService<AutoStartService>();
        autoStartService.AutoStart = item.IsChecked;
        item.IsChecked = autoStartService.AutoStart;
    }
    
    private void ChangeLanguage_Click(object? sender, EventArgs e)
    {
        var item = sender as MenuItem;
        if (item?.Tag is not string languageCode || 
            string.IsNullOrEmpty(languageCode))
        {
            return;
        }

        var languageService = ServiceLocator.GetRequiredService<LanguageService>();
        if (!languageService.SetLanguageForNextStartup(languageCode))
        {
            return;
        }

        var restart = TopmostMessageBox.Show(
            GetCaption("Language.RestartPrompt", "The display language has been saved. Restart MyTools now to apply it?"),
            GetCaption("Language.RestartTitle", "Restart required"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (restart == MessageBoxResult.Yes)
        {
            Restart();
            return;
        }

        UpdateNotifyIconMenu();
    }

    private void ChangeTheme_Click(object? sender, EventArgs e)
    {
        if (sender is not MenuItem { Tag: ThemeKind theme })
        {
            return;
        }

        var themeService = ServiceLocator.GetRequiredService<IThemeService>();
        themeService.SetTheme(theme);
        // Tray menu refresh is handled centrally by OnThemeChanged.
    }

    public void Restart()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Unable to determine the MyTools executable path.");
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"--restart-wait {Environment.ProcessId}",
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        });
        if (process == null)
        {
            throw new InvalidOperationException("Unable to start a new MyTools process.");
        }

        Current.Shutdown();
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        if (_notifyIcon is not null)
        {
            serviceProvider?.GetService<ITrayNotificationService>()?.Detach(_notifyIcon);
        }
        _notifyIcon?.Dispose();
        appBootstrapper?.Dispose();
        serviceProvider?.Dispose();
        if (ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
