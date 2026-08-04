using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Hardcodet.Wpf.TaskbarNotification.Interop;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.DependencyInjection;
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
    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, appName, out var createdNew);
        ownsMutex = createdNew;
        if (!createdNew)
        {
            Console.WriteLine("Another instance is running, shutting down.");
            Current.Shutdown();
            return;
        }
        
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        
        appBootstrapper = new AppBootstrapper();
        appBootstrapper.Init();
        
        InitializeNotifyIcon();
        
        base.OnStartup(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logger = ServiceLocator.GetRequiredService<ILogger<App>>();
        logger.LogError(e.Exception, "Unhandled exception");
        e.Handled = true;
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
            cultureItem.Header = GetCaption($"Language.{culture.Name}", culture.NativeName);
            cultureItem.IsChecked = languageService.CurrentCulture.Name == culture.Name;
            cultureItem.Click += ChangeLanguage_Click;
            cultureItem.Tag = culture.Name;
            languageMenu.Items.Add(cultureItem);
        }
        
        _notifyIcon.ContextMenu.Items.Add(languageMenu);

        var exitItem = new MenuItem { Header = GetCaption("Exit", "Exit") };
        exitItem.Click += (_, _) => Current.Shutdown();
        _notifyIcon.ContextMenu.Items.Add(exitItem);
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        menuItem.IsEnabled = false;
        var originalHeader = menuItem.Header;
        try
        {
            menuItem.Header = GetCaption("CheckingForUpdates", "Checking for updates...");
            var updateService = ServiceLocator.GetRequiredService<IUpdateService>();
            var result = await updateService.CheckForUpdatesAsync();
            switch (result.Status)
            {
                case UpdateCheckStatus.NotConfigured:
                    MessageBox.Show(
                        GetCaption("UpdateNotConfigured", "Configure General.UpdateUrl in Settings before checking for updates."),
                        GetCaption("Info", "Information"), MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case UpdateCheckStatus.NotInstalled:
                    MessageBox.Show(
                        GetCaption("UpdateRequiresInstallation", "Updates are available only when MyTools is installed by Velopack."),
                        GetCaption("Info", "Information"), MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case UpdateCheckStatus.NoUpdate:
                    MessageBox.Show(
                        GetCaption("NoUpdateAvailable", "You are using the latest version ({0}).", result.Version),
                        GetCaption("Info", "Information"), MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case UpdateCheckStatus.Busy:
                    MessageBox.Show(
                        GetCaption("UpdateBusy", "An update operation is already running."),
                        GetCaption("Info", "Information"), MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case UpdateCheckStatus.UpdateAvailable:
                    await ConfirmDownloadAndInstallUpdateAsync(updateService, result.Version, menuItem);
                    break;
            }
        }
        catch (Exception ex)
        {
            var logger = ServiceLocator.GetRequiredService<ILogger<App>>();
            logger.LogError(ex, "Failed to check for or install updates.");
            var message = UpdateService.IsGithubRateLimitException(ex)
                ? GetCaption(
                    "UpdateRateLimitExceeded",
                    "GitHub's update-check request limit has been reached. The proxy IP may be shared by multiple users. Please try again later or switch the update proxy node.")
                : GetCaption("UpdateFailed", "Update failed: {0}", ex.Message);
            MessageBox.Show(
                message,
                GetCaption("Error", "Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            menuItem.Header = originalHeader;
            menuItem.IsEnabled = true;
        }
    }

    private static async Task ConfirmDownloadAndInstallUpdateAsync(
        IUpdateService updateService,
        string? version,
        MenuItem menuItem)
    {
        var answer = MessageBox.Show(
            GetCaption("UpdateAvailable", "Version {0} is available. Download and restart MyTools now?", version),
            GetCaption("CheckForUpdates", "Check for Updates"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var progress = new Progress<int>(value =>
        {
            menuItem.Header = GetCaption("DownloadingUpdate", "Downloading update... {0}%", value);
        });
        await updateService.DownloadAndPrepareUpdateAsync(progress);
        Current.Shutdown();
    }
    
    private void OpenConfigFolder_Click(object? sender, EventArgs e)
    {
        var param = ActionStringParam.From(Path.Combine(ConfigPath.Base, "MyToolsConfig.json"));
        new OpenInExplorer().ExecuteAsync(param);
    }
    
    private void OpenSettings_Click(object? sender, EventArgs e)
    {
        try
        {
            var settingsWindow = new ConfigurationWindow();
            settingsWindow.Show();
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
        _notifyIcon?.Dispose();
        appBootstrapper?.Dispose();
        if (ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}