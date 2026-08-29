namespace MyTools.Host.Core.Bus;

/// <summary>
/// Identity of a connected endpoint within a session. Combines the plugin package id,
/// session id (one run of the plugin) and the per-connection endpoint label. Node endpoints are
/// distinguished from WebView endpoints because routing and capability rules differ.
/// </summary>
public sealed record EndpointId(
    string PluginId,
    string SessionId,
    string EndpointLabel,
    bool IsNode)
{
    public override string ToString() => $"{PluginId}/{SessionId}/{EndpointLabel}";
}
