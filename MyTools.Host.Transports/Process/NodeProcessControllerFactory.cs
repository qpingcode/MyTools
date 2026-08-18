using MyTools.Host.Core.Sessions;

namespace MyTools.Host.Transports.Process;

/// <summary>
/// Default <see cref="INodeProcessControllerFactory"/> producing real
/// <see cref="NodeProcessController"/> instances.
/// </summary>
public sealed class NodeProcessControllerFactory : INodeProcessControllerFactory
{
    private readonly string _pluginsDataRoot;

    public NodeProcessControllerFactory(string pluginsDataRoot)
    {
        _pluginsDataRoot = pluginsDataRoot;
    }

    public INodeProcessController Create(string nodeExePath, string nodeEntryFullPath)
        => new NodeProcessController(nodeExePath, nodeEntryFullPath, _pluginsDataRoot);
}
