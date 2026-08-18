using System.Text.Json;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public interface IPluginHostCapabilityHandler
{
    string Capability { get; }
    Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken);
}
