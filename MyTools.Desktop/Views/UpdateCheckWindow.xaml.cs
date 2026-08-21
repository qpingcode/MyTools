using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Theming;
using MyTools.Desktop.Services;
using MyTools.Desktop.Themes;
using static MyTools.Desktop.Services.LanguageService;

namespace MyTools.Desktop.Views;

/// <summary>
/// Drives the whole check-for-updates flow in a single window:
/// checking (with loading + cancel) -> result (message + action) / downloading (progress).
/// </summary>
public partial class UpdateCheckWindow
{
    private readonly IUpdateService _updateService;
    private readonly ILogger<UpdateCheckWindow> _logger;
    private readonly IThemeService _themeService;
    private readonly CancellationTokenSource _checkCts = new();
    private bool _isChecking;

    public UpdateCheckWindow(IUpdateService updateService)
    {
        InitializeComponent();
        WindowFocusTopmost.Attach(this);

        _updateService = updateService;
        _logger = ServiceLocator.GetRequiredService<ILogger<UpdateCheckWindow>>();
        _themeService = ServiceLocator.GetRequiredService<IThemeService>();
        _themeService.ThemeChanged += ThemeService_ThemeChanged;
        SourceInitialized += Window_SourceInitialized;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowTitleBarTheme.Apply(this, _themeService.CurrentTheme);
    }

    private void ThemeService_ThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        WindowTitleBarTheme.Apply(this, e.CurrentTheme);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_isChecking || _checkCts.IsCancellationRequested)
        {
            return;
        }

        _isChecking = true;
        ShowChecking();

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(_checkCts.Token);
            if (_checkCts.IsCancellationRequested)
            {
                return;
            }

            ShowResult(result);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user; nothing to show.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates.");
            if (_checkCts.IsCancellationRequested)
            {
                return;
            }

            ShowMessage(FormatCheckError(ex), showRetry: true);
        }
        finally
        {
            _isChecking = false;
        }
    }

    private void ShowChecking()
    {
        ResultState.Visibility = Visibility.Collapsed;
        DownloadingState.Visibility = Visibility.Collapsed;
        CheckingState.Visibility = Visibility.Visible;
    }

    private void ShowResult(UpdateCheckResult result)
    {
        switch (result.Status)
        {
            case UpdateCheckStatus.NotConfigured:
                ShowMessage(GetCaption("UpdateNotConfigured", "Configure General.UpdateUrl in Settings before checking for updates."));
                break;
            case UpdateCheckStatus.NotInstalled:
                ShowMessage(GetCaption("UpdateRequiresInstallation", "Updates are available only when MyTools is installed by Velopack."));
                break;
            case UpdateCheckStatus.NoUpdate:
                ShowMessage(GetCaption("NoUpdateAvailable", "You are using the latest version ({0}).", result.Version));
                break;
            case UpdateCheckStatus.Busy:
                ShowMessage(GetCaption("UpdateBusy", "An update operation is already running."), showRetry: true);
                break;
            case UpdateCheckStatus.UpdateAvailable:
                ShowMessage(GetCaption("UpdateAvailable", "Version {0} is available. Download and restart MyTools now?", result.Version), showDownload: true);
                break;
        }
    }

    private void ShowMessage(string message, bool showDownload = false, bool showRetry = false)
    {
        ResultMessage.Text = message;
        RetryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Visibility = showDownload ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.IsEnabled = true;
        OkButton.Visibility = showDownload ? Visibility.Collapsed : Visibility.Visible;
        OkButton.Style = (Style)FindResource(showRetry ? "CancelButtonStyle" : "ModernButton");

        CheckingState.Visibility = Visibility.Collapsed;
        DownloadingState.Visibility = Visibility.Collapsed;
        ResultState.Visibility = Visibility.Visible;
    }

    private static string FormatCheckError(Exception ex)
    {
        return UpdateService.IsGithubRateLimitException(ex)
            ? GetCaption(
                "UpdateRateLimitExceeded",
                "GitHub's update-check request limit has been reached. The proxy IP may be shared by multiple users. Please try again later or switch the update proxy node.")
            : GetCaption("UpdateFailed", "Update failed: {0}", ex.Message);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;

        var progress = new Progress<int>(value =>
        {
            DownloadProgress.Value = value;
            DownloadingText.Text = GetCaption("DownloadingUpdate", "Downloading update... {0}%", value);
        });

        // Default text before the first progress report arrives.
        DownloadingText.Text = GetCaption("DownloadingUpdate", "Downloading update... {0}%", 0);
        DownloadProgress.Value = 0;

        CheckingState.Visibility = Visibility.Collapsed;
        ResultState.Visibility = Visibility.Collapsed;
        DownloadingState.Visibility = Visibility.Visible;

        try
        {
            await _updateService.DownloadAndPrepareUpdateAsync(progress);
            // Velopack applies the update and restarts on exit.
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download the update.");
            ShowMessage(FormatCheckError(ex), showRetry: true);
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _checkCts.Cancel();
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _themeService.ThemeChanged -= ThemeService_ThemeChanged;
        SourceInitialized -= Window_SourceInitialized;

        // If the user closes the window while still checking, cancel the in-flight check.
        if (ResultState.Visibility != Visibility.Visible && DownloadingState.Visibility != Visibility.Visible)
        {
            _checkCts.Cancel();
        }

        _checkCts.Dispose();
    }
}
