using System.Text.Json;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginDetailContext
{
    public required NodePlugin Plugin { get; init; }
    public required string PluginId { get; init; }
    public required string Version { get; init; }
    public required string ProtocolVersion { get; init; }
    public required string PluginDirectory { get; init; }
    public required string EntryFullPath { get; init; }
    public required string ItemId { get; init; }
    public required string SearchText { get; init; }
    public required string Query { get; init; }
    public required string Keyword { get; init; }
    public required JsonElement InitialState { get; init; }
    public required string Locale { get; init; }
    public required string FallbackLocale { get; init; }
    public required IReadOnlyDictionary<string, string> Messages { get; init; }
}