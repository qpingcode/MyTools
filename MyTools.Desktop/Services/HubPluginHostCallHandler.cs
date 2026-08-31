using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using MyTools.Common.Config;
using MyTools.Common.Localization;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class HubPluginHostCallHandler(
    HubAccountService accounts,
    HubMarketplaceService marketplace,
    HubSyncService sync,
    ILocalizationService localization,
    NodePluginCatalog catalog,
    PluginLoader pluginLoader) : IPluginHostCapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyCollection<string> Capabilities { get; } =
    [
        "account.status", "account.login", "account.register", "account.logout", "account.externalLogin",
        "marketplace.search", "marketplace.get", "marketplace.install", "marketplace.uninstall", "marketplace.publish.validate", "marketplace.publish",
        "sync.pull", "sync.push"
    ];

    public async Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        return request.Method switch
        {
            "account.status" => Json(await accounts.GetStatusAsync(cancellationToken)),
            "account.login" => Json(await SignInAsync(() => accounts.LoginAsync(ReadString(request.Params, "username"), ReadString(request.Params, "password"), cancellationToken), cancellationToken)),
            "account.register" => Json(await SignInAsync(() => accounts.RegisterAsync(ReadString(request.Params, "username"), ReadString(request.Params, "password"), cancellationToken), cancellationToken)),
            "account.externalLogin" => Json(await SignInAsync(() => accounts.LoginWithExternalAsync(ReadString(request.Params, "provider"), cancellationToken), cancellationToken)),
            "account.logout" => Json(accounts.Logout()),
            "marketplace.search" => Json(await SearchMarketplaceSafeAsync(request.Params, cancellationToken)),
            "marketplace.get" => Json(AttachInstallState(await marketplace.GetAsync(ReadString(request.Params, "pluginId"), TryReadString(request.Params, "locale") ?? localization.CurrentLocale, cancellationToken))),
            "marketplace.install" => Json(await marketplace.InstallAsync(ReadString(request.Params, "pluginId"), TryReadString(request.Params, "version"), cancellationToken)),
            "marketplace.uninstall" => Json(await UninstallAsync(ReadString(request.Params, "pluginId"), cancellationToken)),
            "marketplace.publish.validate" => Json(await marketplace.ValidateDevelopmentPublishAsync(ReadString(request.Params, "pluginId"), TryReadString(request.Params, "version"), cancellationToken)),
            "marketplace.publish" => Json(await marketplace.PublishDevelopmentAsync(ReadString(request.Params, "pluginId"), TryReadString(request.Params, "version"), cancellationToken)),
            "sync.pull" => Json(await sync.PullAsync(cancellationToken)),
            "sync.push" => Json(await sync.PushAsync(cancellationToken)),
            _ => throw new NotSupportedException($"Unknown hub hostCall method: {request.Method}")
        };
    }

    private async Task<HubPluginList> SearchMarketplaceSafeAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var query = TryReadString(payload, "query");
        var locale = TryReadString(payload, "locale") ?? localization.CurrentLocale;
        try
        {
            return AttachInstallState(await marketplace.SearchAsync(query, cancellationToken, locale));
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException
                                              {
                                                  SocketErrorCode: SocketError.ConnectionRefused
                                              })
        {
            // If local Hub service is not running, keep global search responsive.
            return new HubPluginList();
        }
    }

    private HubPluginList AttachInstallState(HubPluginList list)
    {
        foreach (var item in list.Items)
        {
            AttachInstallState(item);
        }

        return list;
    }

    private HubPluginDetail AttachInstallState(HubPluginDetail detail)
    {
        AttachInstallState((HubPluginSummary)detail);
        return detail;
    }

    private void AttachInstallState(HubPluginSummary plugin)
    {
        var installedVersion = FindInstalledVersion(plugin.Id);
        plugin.InstalledVersion = installedVersion;
        plugin.Installed = !string.IsNullOrWhiteSpace(installedVersion);
        plugin.UpdateAvailable = plugin.Installed
            && SemanticVersion.TryParse(installedVersion!, out var local)
            && SemanticVersion.TryParse(plugin.CurrentVersion, out var remote)
            && local.CompareTo(remote) < 0;
        plugin.CanUninstall = !string.IsNullOrWhiteSpace(ReadInstalledVersionFromDisk(plugin.Id))
            && !string.Equals(plugin.Id, "store", StringComparison.OrdinalIgnoreCase);
    }

    private string? FindInstalledVersion(string pluginId)
    {
        return ReadInstalledVersionFromDisk(pluginId) ?? FindCatalogVersion(pluginId);
    }

    private string? FindCatalogVersion(string pluginId)
    {
        foreach (var manifest in catalog.Plugins)
        {
            if (string.Equals(manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(manifest.Version) ? null : manifest.Version;
            }
        }

        return null;
    }

    private static string? ReadInstalledVersionFromDisk(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)
            || pluginId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        var pluginsRoot = Path.GetFullPath(Path.Combine(ConfigPath.Base, "plugins"));
        var target = Path.GetFullPath(Path.Combine(pluginsRoot, pluginId));
        var prefix = pluginsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var manifestPath = Path.Combine(target, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var version = document.RootElement.TryGetProperty("version", out var value)
                ? value.GetString()
                : null;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task<HubAccountStatus> SignInAsync(Func<Task<HubAccountStatus>> signIn, CancellationToken cancellationToken)
    {
        var status = await signIn();
        try
        {
            await sync.PullAsync(cancellationToken);
        }
        catch
        {
            /* first login may have nothing to pull */
        }

        return status;
    }

    private async Task<object> UninstallAsync(string pluginId, CancellationToken cancellationToken)
    {
        await pluginLoader.UnloadPluginAsync(pluginId);
        await marketplace.UninstallAsync(pluginId, cancellationToken);
        return new { success = true, pluginId };
    }

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    private static string ReadString(JsonElement payload, string name)
    {
        var value = TryReadString(payload, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing '{name}'.");
        }

        return value;
    }

    private static string? TryReadString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}
