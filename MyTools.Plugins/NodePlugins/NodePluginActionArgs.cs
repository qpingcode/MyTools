using MyTools.Common;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginActionArgs : IActionParams
{
    public NodePluginActionArgs(string itemId, string query)
    {
        ItemId = itemId;
        Query = query;
    }

    public string ItemId { get; }

    public string Query { get; }
}