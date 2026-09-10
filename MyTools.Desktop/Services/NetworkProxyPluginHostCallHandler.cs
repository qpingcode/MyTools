using System.Text.Json;
using MyTools.AI;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class NetworkProxyPluginHostCallHandler(IPluginCreationProxyProvider proxyProvider)
    : IPluginHostCapabilityHandler
{
    public IReadOnlyCollection<string> Capabilities { get; } = ["network.proxy.read"];

    public Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Method, "network.proxy.read", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Unknown network proxy hostCall method: {request.Method}");

        var settings = proxyProvider.GetProxySettings();
        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            proxyUrl = settings.ProxyUri?.AbsoluteUri ?? string.Empty
        }));
    }
}
