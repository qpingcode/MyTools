using MyTools.Common.Localization;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginManifest
{
    public string Id { get; init; } = string.Empty;
    public LocalizedMessage? NameMessage { get; init; }

    /// <summary>
    /// 未翻译的显示名称（NameMessage.DefaultValue），fallback 到 EntryId。
    /// 便捷属性，供日志、调试等不需要翻译的场景使用。
    /// </summary>
    public string Name => NameMessage?.DefaultValue ?? EntryId;

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
    /// <summary>Capability ids declared by this entry (e.g. <c>configuration.write</c>).</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public string DefaultLocale { get; init; } = "en-US";
    public string? CatalogFullPath { get; init; }
    public string? LocalesDirectoryFullPath { get; init; }
    public IReadOnlyList<string> SupportedLocales { get; init; } = [];
}