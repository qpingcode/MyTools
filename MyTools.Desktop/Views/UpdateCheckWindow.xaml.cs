using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using MyTools.Common.DependencyInjection;
using MyTools.Desktop.Services;
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
    private readonly CancellationTokenSource _checkCts = new();

    public UpdateCheckWindow(IUpdateService updateService)
    {
        InitializeComponent();

        _updateService = updateService;
        _logger = ServiceLocator.GetRequiredService<ILogger<UpdateCheckWindow>>();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _updateService.CheckForUpdatesAsync(_checkCts.Token);
            if (_checkCts.IsCancellationRequested)
            {
                // User cancelled while checking; the window is already closing.
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
            var message = UpdateService.IsGithubRateLimitException(ex)
                ? GetCaption(
                    "UpdateRateLimitExceeded",
                    "GitHub's update-check request limit has been reached. The proxy IP may be shared by multiple users. Please try again later or switch the update proxy node.")
                : GetCaption("UpdateFailed", "Update failed: {0}", ex.Message);
            ShowMessage(message);
        }
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
                ShowMessage(GetCaption("UpdateBusy", "An update operation is already running."));
                break;
            case UpdateCheckStatus.UpdateAvailable:
                ShowMessage(GetCaption("UpdateAvailable", "Version {0} is available. Download and restart MyTools now?", result.Version), showDownload: true);
                break;
        }
    }

    /// <summary>
    /// Switches to the result state with the given message.
    /// </summary>
    private void ShowMessage(string message, bool showDownload = false)
    {
        ResultMessage.Text = message;
        DownloadButton.Visibility = showDownload ? Visibility.Visible : Visibility.Collapsed;

        CheckingState.Visibility = Visibility.Collapsed;
        DownloadingState.Visibility = Visibility.Collapsed;
        ResultState.Visibility = Visibility.Visible;
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
            var message = UpdateService.IsGithubRateLimitException(ex)
                ? GetCaption(
                    "UpdateRateLimitExceeded",
                    "GitHub's update-check request limit has been reached. The proxy IP may be shared by multiple users. Please try again later or switch the update proxy node.")
                : GetCaption("UpdateFailed", "Update failed: {0}", ex.Message);
            ShowMessage(message);
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
        // If the user closes the window while still checking, cancel the in-flight check.
        if (ResultState.Visibility != Visibility.Visible && DownloadingState.Visibility != Visibility.Visible)
        {
            _checkCts.Cancel();
        }

        _checkCts.Dispose();
    }
}
