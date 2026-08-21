using System.Text.Json;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class DevelopmentPluginHostCallHandler : IPluginHostCapabilityHandler
{
    private readonly DevelopmentPluginService service;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public DevelopmentPluginHostCallHandler(DevelopmentPluginService service)
    {
        this.service = service;
    }

    public IReadOnlyCollection<string> Capabilities { get; } =
    [
        "development.create", "development.list",
        "development.refresh", "development.openFolder", "development.openCode"
    ];

    public Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        var result = request.Method switch
        {
            "development.create" => Create(request.Params),
            "development.list" => JsonSerializer.SerializeToElement(new { plugins = service.GetRegistrations() }, JsonOptions),
            "development.refresh" => Refresh(),
            "development.openFolder" => Open(request.Params, DevelopmentPluginService.OpenFolder),
            "development.openCode" => Open(request.Params, DevelopmentPluginService.OpenVisualStudioCode),
            _ => throw new NotSupportedException($"Unknown development hostCall method: {request.Method}")
        };
        return Task.FromResult(result);
    }

    private JsonElement Create(JsonElement payload)
    {
        var createRequest = payload.Deserialize<CreateDevelopmentPluginRequest>(JsonOptions)
            ?? throw new InvalidOperationException("Invalid create plugin request.");
        return JsonSerializer.SerializeToElement(service.Create(createRequest), JsonOptions);
    }

    private JsonElement Refresh()
    {
        service.RefreshAll();
        return JsonSerializer.SerializeToElement(new { success = true }, JsonOptions);
    }

    private static JsonElement Open(JsonElement payload, Action<string> open)
    {
        open(payload.GetProperty("sourcePath").GetString() ?? "");
        return JsonSerializer.SerializeToElement(new { success = true }, JsonOptions);
    }
}
