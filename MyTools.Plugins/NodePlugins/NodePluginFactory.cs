using Microsoft.Extensions.Logging;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginFactory
{
    private readonly ILoggerFactory loggerFactory;

    public NodePluginFactory(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
    }

    public IReadOnlyList<NodePlugin> CreatePlugins(IEnumerable<NodePluginManifest> manifests)
    {
        return manifests
            .Select(manifest =>
            {
                var processHost = new NodePluginProcessHost(manifest, loggerFactory.CreateLogger<NodePluginProcessHost>());
                return new NodePlugin(manifest, processHost, loggerFactory.CreateLogger<NodePlugin>());
            })
            .ToList();
    }
}