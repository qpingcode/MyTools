using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class NodePluginDetailNavigator : INodePluginDetailNavigator
{
    public event Action<NodePluginDetailContext>? DetailRequested;

    public void OpenDetail(NodePluginDetailContext context)
    {
        DetailRequested?.Invoke(context);
    }
}