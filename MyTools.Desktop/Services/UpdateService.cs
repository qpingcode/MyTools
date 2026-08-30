using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config.Interfaces;
using Velopack;
using Velopack.Exceptions;
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
    public const string DefaultChannel = "stable";
    public const string BetaChannel = "beta";
    private static readonly HashSet<string> SupportedProxySchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "socks4",
        "socks4a",
        "socks5"
    };

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

            var updateUrl = GetStringSetting(GeneralSettings.UpdateUrl);
            if (string.IsNullOrWhiteSpace(updateUrl))
            {
                return new UpdateCheckResult(UpdateCheckStatus.NotConfigured);
            }

            if (!VelopackLocator.IsCurrentSet)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NotInstalled);
            }

            var channel = ResolveChannel(GetStringSetting(GeneralSettings.UpdateChannel));
            var includePrereleases = IncludeGitHubPrereleases(channel);
            var options = new UpdateOptions
            {
                ExplicitChannel = channel,
                AllowVersionDowngrade = true
            };
            var proxyUri = ParseProxyUri(GetStringSetting(GeneralSettings.UpdateProxyUrl));
            var updateManager = CreateUpdateManager(updateUrl, options, proxyUri, includePrereleases);
            if (!updateManager.IsInstalled)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NotInstalled);
            }

            logger.LogInformation(
                "Checking the configured update source on channel {Channel} ({ConnectionMode}, prerelease={IncludePrereleases}).",
                options.ExplicitChannel,
                proxyUri == null ? "direct connection" : "proxy",
                includePrereleases);
            cancellationToken.ThrowIfCancellationRequested();

            // Velopack's CheckForUpdatesAsync does not honor our cancellation token, so race it
            // against cancellation. On cancel we return immediately (releasing the lock) and let
            // the in-flight network call finish in the background; it uses its own UpdateManager
            // instance and does not affect the next check.
            var checkTask = updateManager.CheckForUpdatesAsync();
            var completed = await Task.WhenAny(checkTask, Task.Delay(Timeout.Infinite, cancellationToken));
            if (completed != checkTask)
            {
                // Cancelled before the network call returned.
                _ = checkTask.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
                cancellationToken.ThrowIfCancellationRequested();
            }

            UpdateInfo? update;
            try
            {
                update = await checkTask;
            }
            catch (NotInstalledException)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NotInstalled);
            }
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

    internal static string ResolveChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return DefaultChannel;
        }

        return channel.Trim();
    }

    internal static bool IncludeGitHubPrereleases(string channel)
    {
        return channel.Equals(BetaChannel, StringComparison.OrdinalIgnoreCase);
    }

    private static UpdateManager CreateUpdateManager(
        string updateUrl,
        UpdateOptions options,
        Uri? proxyUri,
        bool includeGitHubPrereleases)
    {
        var trimmedUpdateUrl = updateUrl.Trim();
        var githubRepositoryUrl = GetGithubRepositoryUrl(trimmedUpdateUrl);
        var downloader = new UpdateProxyFileDownloader(proxyUri);
        if (githubRepositoryUrl != null)
        {
            return new UpdateManager(
                new GithubSource(githubRepositoryUrl, null, includeGitHubPrereleases, downloader),
                options);
        }

        return Uri.TryCreate(trimmedUpdateUrl, UriKind.Absolute, out var updateUri)
            && (updateUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || updateUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            ? new UpdateManager(new SimpleWebSource(updateUri, downloader), options)
            : new UpdateManager(trimmedUpdateUrl, options);
    }

    internal static Uri? ParseProxyUri(string? proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl))
        {
            return null;
        }

        var trimmedProxyUrl = proxyUrl.Trim();
        if (!Uri.TryCreate(trimmedProxyUrl, UriKind.Absolute, out var proxyUri)
            || !SupportedProxySchemes.Contains(proxyUri.Scheme)
            || string.IsNullOrWhiteSpace(proxyUri.Host))
        {
            throw new InvalidOperationException(
                "The proxy URL must be an absolute HTTP, HTTPS, SOCKS4, SOCKS4A, or SOCKS5 URL.");
        }

        if (!string.IsNullOrEmpty(proxyUri.UserInfo))
        {
            throw new InvalidOperationException("The proxy URL must not contain a username or password.");
        }

        return proxyUri;
    }

    internal static bool IsGithubRateLimitException(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException
                {
                    StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                }
                && current.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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


