using System.Text.Json;
using MyTools.AI;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class LocationPluginHostCallHandler(IHostCityProvider cityProvider) : IPluginHostCapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyCollection<string> Capabilities { get; } = ["location.city"];

    public async Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Method, "location.city", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Unknown location hostCall method: {request.Method}");
        return JsonSerializer.SerializeToElement(
            await cityProvider.GetCityAsync(cancellationToken),
            JsonOptions);
    }
}
