using System.Collections.Generic;
using System.Text.Json;

namespace MyTools.Plugins.NodePlugins;

internal sealed class NodePluginInitializeRequest
{
    public required string Locale { get; init; }
    public required string FallbackLocale { get; init; }
    public required IReadOnlyDictionary<string, string> Messages { get; init; }
    public required string Theme { get; init; }
}

internal sealed class NodePluginSearchResponse
{
    public List<NodePluginSearchItem> Items { get; init; } = [];
}

internal sealed class NodePluginSearchItem
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public int Priority { get; init; }
    public NodePluginIconDto? Icon { get; init; }
    public List<NodePluginActionDto> Actions { get; init; } = [];
}

internal sealed class NodePluginIconDto
{
    public string Kind { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

internal sealed class NodePluginActionDto
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Kind { get; init; } = "invoke";
    public string Description { get; init; } = string.Empty;
}

internal sealed class NodePluginActionResponse
{
    public string Message { get; init; } = string.Empty;
    public string ActionType { get; init; } = "none";
    public NodePluginDetailViewDto? Detail { get; init; }
}

internal sealed class NodePluginDetailViewDto
{
    public string Type { get; init; } = "web-detail";
    public string HtmlEntry { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public JsonElement InitialState { get; init; }
}

public sealed class NodePluginEventReceivedEventArgs : EventArgs
{
    public required string SubjectId { get; init; }
    public required JsonElement Payload { get; init; }
}