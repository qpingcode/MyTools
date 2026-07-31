namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginManifest
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public string Entry { get; init; } = string.Empty;
    public string ProtocolVersion { get; init; } = string.Empty;
    public string PluginDirectory { get; init; } = string.Empty;
    public string EntryFullPath { get; init; } = string.Empty;
    public string ParentId { get; init; } = string.Empty;
    public string EntryId { get; init; } = string.Empty;
    public string? DetailEntry { get; init; }
    public string? DetailEntryFullPath { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string? HotKey { get; init; }
}