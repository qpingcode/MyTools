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

internal sealed class NodePluginSearchRequest
{
    public required string Query { get; init; }
    public required string Mode { get; init; }
    public required string Locale { get; init; }
    public required string FallbackLocale { get; init; }
    public required string Theme { get; init; }
}

internal sealed class NodePluginActionRequest
{
    public required string ItemId { get; init; }
    public required string ActionId { get; init; }
    public required string Query { get; init; }
    public required string Locale { get; init; }
    public required string FallbackLocale { get; init; }
    public required string Theme { get; init; }
}

/// <summary>
/// plugin.call.initialize 的响应。插件在启动时一次性声明它拥有的全部 action；
/// 之后搜索结果项和详情页只按 id 引用，宿主不再从 item 上猜 action 的参数。
/// </summary>
internal sealed class NodePluginInitializeResponse
{
    public List<NodePluginActionDefinitionDto> Actions { get; init; } = [];
}

internal sealed class NodePluginActionDefinitionDto
{
    public string Id { get; init; } = string.Empty;
    public NodePluginLocalizedTextDto? Title { get; init; }
    public NodePluginLocalizedTextDto? Description { get; init; }

    /// <summary>显式快捷键。为空时，当前 action 子集的第一项使用 Enter。</summary>
    public NodePluginHotkeyDto? Hotkey { get; init; }
}

internal sealed class NodePluginHotkeyDto
{
    public string Key { get; init; } = string.Empty;
    public int Modifiers { get; init; }
}

public sealed class NodePluginWebActionDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Hotkey { get; init; }
}

internal sealed class NodePluginLocalizedTextDto
{
    public string Key { get; init; } = string.Empty;
    public string DefaultValue { get; init; } = string.Empty;
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

    /// <summary>引用 initialize 注册表里的 action id，按展示顺序排列。</summary>
    public List<string> Actions { get; init; } = [];
}

internal sealed class NodePluginIconDto
{
    public string Kind { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// plugin.call.invokeAction 的响应。各字段可组合：例如"打开 IDE 并关闭搜索窗"
/// 就是 <c>Host</c> + <c>Close</c>。
/// </summary>
internal sealed class NodePluginActionResponse
{
    public NodePluginLocalizedTextDto? Message { get; init; }
    public bool Close { get; init; }
    public NodePluginHostActionDto? Host { get; init; }
    public NodePluginWebActionDto? Web { get; init; }
    public NodePluginDetailViewDto? Detail { get; init; }
}

/// <summary>
/// 宿主侧要执行的动作。字段随 <see cref="Kind"/> 而定，由
/// <see cref="NodePluginWellKnownActions"/> 按 kind 读取对应字段，不做跨字段回退。
/// </summary>
internal sealed class NodePluginHostActionDto
{
    public string Kind { get; init; } = string.Empty;

    /// <summary>copy / copyAndPaste</summary>
    public string? Text { get; init; }

    /// <summary>execute / openInExplorer</summary>
    public string? Path { get; init; }

    /// <summary>execute</summary>
    public string? Args { get; init; }

    /// <summary>execute</summary>
    public bool RunAsAdmin { get; init; }

    /// <summary>openInBrowser，string 或 string[]</summary>
    public JsonElement Url { get; init; }

    /// <summary>openPlugin</summary>
    public string? PluginId { get; init; }

    /// <summary>run，CommandSpec 对象</summary>
    public JsonElement Command { get; init; }

    /// <summary>kill</summary>
    public int Pid { get; init; }
}

/// <summary>把处理权交给详情页网页，payload 原样透传为 host.event.detailAction。</summary>
internal sealed class NodePluginWebActionDto
{
    public JsonElement Payload { get; init; }
}

internal sealed class NodePluginDetailViewDto
{
    public string Type { get; init; } = "web-detail";

    /// <summary>相对插件目录的 html 入口；为空时用 plugin.json 里声明的 detail.entry。</summary>
    public string Page { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public JsonElement InitialState { get; init; }
}

public sealed class NodePluginEventReceivedEventArgs : EventArgs
{
    public required string SubjectId { get; init; }
    public required JsonElement Payload { get; init; }
}
