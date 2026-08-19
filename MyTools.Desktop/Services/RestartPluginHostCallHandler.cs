using System.Text.Json;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class RestartPluginHostCallHandler : IPluginHostCapabilityHandler
{
    public IReadOnlyCollection<string> Capabilities { get; } = ["restart"];

    public Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Method, "restart", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Unknown restart hostCall method: {request.Method}");
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (System.Windows.Application.Current is App app)
            {
                app.Restart();
            }
        });

        return Task.FromResult(JsonSerializer.SerializeToElement(new { }));
    }
}
