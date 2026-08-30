using MyTools.Common.Localization;
using MyTools.Protocol.Manifest;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginManifest
{
    public string Id { get; init; } = string.Empty;
    public LocalizedMessage? NameMessage { get; init; }
    public LocalizedMessage? DescriptionMessage { get; init; }

    /// <summary>
    /// 未翻译的显示名称（NameMessage.DefaultValue），fallback 到 Id。
    /// 便捷属性，供日志、调试等不需要翻译的场景使用。
    /// </summary>
    public string Name => NameMessage?.DefaultValue ?? Id;

    public string Version { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public string Entry { get; init; } = string.Empty;
    public string ProtocolVersion { get; init; } = string.Empty;
    public string PluginDirectory { get; init; } = string.Empty;
    public string EntryFullPath { get; init; } = string.Empty;
    /// <summary>Relative web detail path when <c>detail.type</c> is <c>web</c>; otherwise null (native list).</summary>
    public string? DetailEntry { get; init; }
    /// <summary>Absolute web detail path, or null when this entry uses the native list UI.</summary>
    public string? DetailEntryFullPath { get; init; }
    public bool HasWebDetail => !string.IsNullOrWhiteSpace(DetailEntryFullPath);
    /// <summary>
    /// Action ids that stay visible in the host action bar instead of the overflow menu.
    /// Empty means the existing single-default behavior.
    /// </summary>
    public IReadOnlyList<string> PinnedActions { get; init; } = [];
    /// <summary>Show the host status bar in a standalone PluginWindow. Defaults to true.</summary>
    public bool ShowStatusBarInPluginWindow { get; init; } = true;
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Participate in unscoped (global) search. Default false when omitted in plugin.json.</summary>
    public bool SearchGlobal { get; init; }
    public string? HotKey { get; init; }
    /// <summary>Capability ids declared by this plugin (e.g. <c>configuration.write</c>).</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<PluginConfigurationSettingV3> Configuration { get; init; } = [];
    /// <summary>Settings sidebar MDI icon from plugin.json <c>icon</c>.</summary>
    public string? Icon { get; init; }
    public string DefaultLocale { get; init; } = "en-US";
    public string? CatalogFullPath { get; init; }
    public string? LocalesDirectoryFullPath { get; init; }
    public IReadOnlyList<string> SupportedLocales { get; init; } = [];
}
