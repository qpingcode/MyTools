using System.Text.Json;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class HubPluginHostCallHandler(
    HubAccountService accounts,
    HubMarketplaceService marketplace,
    HubSyncService sync) : IPluginHostCapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyCollection<string> Capabilities { get; } =
    [
        "account.status", "account.login", "account.register", "account.logout", "account.externalLogin",
        "marketplace.search", "marketplace.get", "marketplace.install", "marketplace.publish.validate", "marketplace.publish",
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
            "marketplace.search" => Json(await marketplace.SearchAsync(TryReadString(request.Params, "query"), cancellationToken)),
            "marketplace.get" => Json(await marketplace.GetAsync(ReadString(request.Params, "pluginId"), cancellationToken)),
            "marketplace.install" => Json(await marketplace.InstallAsync(ReadString(request.Params, "pluginId"), TryReadString(request.Params, "version"), cancellationToken)),
            "marketplace.publish.validate" => Json(await marketplace.ValidateDevelopmentPublishAsync(ReadString(request.Params, "pluginId"), cancellationToken)),
            "marketplace.publish" => Json(await marketplace.PublishDevelopmentAsync(ReadString(request.Params, "pluginId"), cancellationToken)),
            "sync.pull" => Json(await sync.PullAsync(cancellationToken)),
            "sync.push" => Json(await sync.PushAsync(cancellationToken)),
            _ => throw new NotSupportedException($"Unknown hub hostCall method: {request.Method}")
        };
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
