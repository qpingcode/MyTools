using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Plugins.NodePlugins;
using MyTools.Protocol.Routing;

namespace MyTools.Desktop.Services;

public sealed class NodePluginHostCallRouter
{
    private readonly IReadOnlyDictionary<string, IPluginHostCapabilityHandler> _handlersByCapability;
    private readonly ILogger<NodePluginHostCallRouter> _logger;

    public NodePluginHostCallRouter(
        IEnumerable<IPluginHostCapabilityHandler> handlers,
        ILogger<NodePluginHostCallRouter> logger)
    {
        _logger = logger;
        var map = new Dictionary<string, IPluginHostCapabilityHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (var handler in handlers)
        {
            foreach (var capability in handler.Capabilities)
            {
                if (map.TryGetValue(capability, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate host-call capability handler for '{capability}': {existing.GetType().Name}, {handler.GetType().Name}");
                }
                map[capability] = handler;
            }
        }
        _handlersByCapability = map;
    }

    public bool HasHandlerForPlugin(NodePlugin plugin)
    {
        foreach (var capability in plugin.Capabilities)
        {
            if (_handlersByCapability.ContainsKey(capability))
            {
                return true;
            }
        }
        return false;
    }

    public async Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        var capability = Routes.StripHostCall($"host.call.{request.Method}");
        if (!_handlersByCapability.TryGetValue(capability, out var handler))
        {
            throw new NotSupportedException(
                $"No host-call handler registered for capability '{capability}' (method '{request.Method}').");
        }

        _logger.LogDebug(
            "Routing hostCall method={Method} capability={Capability} plugin={PluginId}",
            request.Method, capability, request.PluginId);

        return await handler.HandleAsync(request, cancellationToken);
    }
}
