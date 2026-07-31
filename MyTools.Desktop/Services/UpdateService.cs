using System.Reflection;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config.Interfaces;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace MyTools.Desktop.Services;

public enum UpdateCheckStatus
{
    NotConfigured,
    NotInstalled,
    NoUpdate,
    UpdateAvailable,
    Busy
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string? Version = null);

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task DownloadAndPrepareUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class UpdateService(
    IConfigurationRegistry configurationRegistry,
    ILogger<UpdateService> logger) : IUpdateService
{
    public const string DefaultUpdateUrl = "https://github.com/qpingcode/MyTools/releases";
    private const string UpdateUrlSettingPath = "General.UpdateUrl";
    private const string UpdateChannelSettingPath = "General.UpdateChannel";
    private const string DefaultChannel = "win";

    private readonly SemaphoreSlim operationLock = new(1, 1);
    private UpdateManager? pendingUpdateManager;
    private UpdateInfo? pendingUpdate;

    public string CurrentVersion
    {
        get
        {
            var informationalVersion = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return informationalVersion?.Split('+')[0] ?? "unknown";
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            return new UpdateCheckResult(UpdateCheckStatus.Busy);
        }

        try
        {
            pendingUpdateManager = null;
            pendingUpdate = null;

            var updateUrl = GetStringSetting(UpdateUrlSettingPath);
            if (string.IsNullOrWhiteSpace(updateUrl))
            {
                return new UpdateCheckResult(UpdateCheckStatus.NotConfigured);
            }

            if (!VelopackLocator.IsCurrentSet)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NotInstalled);
            }

            var channel = GetStringSetting(UpdateChannelSettingPath);
            var options = new UpdateOptions
            {
                ExplicitChannel = string.IsNullOrWhiteSpace(channel) ? DefaultChannel : channel.Trim()
            };
            var updateManager = CreateUpdateManager(updateUrl, options);
            if (!updateManager.IsInstalled)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NotInstalled);
            }

            logger.LogInformation("Checking the configured update source on channel {Channel}.", options.ExplicitChannel);
            cancellationToken.ThrowIfCancellationRequested();
            var update = await updateManager.CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (update == null)
            {
                logger.LogInformation("No update is available. Current version: {CurrentVersion}.", CurrentVersion);
                return new UpdateCheckResult(UpdateCheckStatus.NoUpdate, CurrentVersion);
            }

            pendingUpdateManager = updateManager;
            pendingUpdate = update;
            var targetVersion = update.TargetFullRelease.Version.ToString();
            logger.LogInformation("Update {TargetVersion} is available.", targetVersion);
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, targetVersion);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task DownloadAndPrepareUpdateAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var updateManager = pendingUpdateManager
                ?? throw new InvalidOperationException("Check for updates before downloading an update.");
            var update = pendingUpdate
                ?? throw new InvalidOperationException("No update is available to download.");

            logger.LogInformation("Downloading update {TargetVersion}.", update.TargetFullRelease.Version);
            await updateManager.DownloadUpdatesAsync(
                update,
                value => progress?.Report(value),
                cancellationToken);

            logger.LogInformation("Update {TargetVersion} is ready. Waiting for the application to exit.", update.TargetFullRelease.Version);
            updateManager.WaitExitThenApplyUpdates(
                update.TargetFullRelease,
                silent: false,
                restart: true,
                restartArgs: []);
        }
        finally
        {
            operationLock.Release();
        }
    }

    private string? GetStringSetting(string path)
    {
        return configurationRegistry.FindSetting(path)?.GetValue<string>();
    }

    private static UpdateManager CreateUpdateManager(string updateUrl, UpdateOptions options)
    {
        var trimmedUpdateUrl = updateUrl.Trim();
        var githubRepositoryUrl = GetGithubRepositoryUrl(trimmedUpdateUrl);
        return githubRepositoryUrl == null
            ? new UpdateManager(trimmedUpdateUrl, options)
            // ReSharper disable once RedundantArgumentDefaultValue
            : new UpdateManager(new GithubSource(githubRepositoryUrl, null, false, null), options);
    }

    internal static string? GetGithubRepositoryUrl(string updateUrl)
    {
        if (!Uri.TryCreate(updateUrl, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pathSegments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length == 3 && pathSegments[2].Equals("releases", StringComparison.OrdinalIgnoreCase))
        {
            pathSegments = pathSegments[..2];
        }

        if (pathSegments.Length != 2)
        {
            return null;
        }

        return $"https://github.com/{pathSegments[0]}/{pathSegments[1]}";
    }
}


