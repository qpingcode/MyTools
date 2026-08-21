using System.Collections.Generic;
using System.Linq;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Versioning;

namespace MyTools.Protocol.Manifest;

/// <summary>
/// v3 plugin manifest. Each entry declares its required capabilities explicitly; the capability
/// gateway rejects undeclared calls even for trusted plugins.
/// </summary>
public sealed class PluginManifestV3
{
    public required string Id { get; init; }
    public string Version { get; init; } = "0.0.0";
    public required string ProtocolVersion { get; init; }
    public required IReadOnlyList<PluginEntryV3> Entries { get; init; }
    /// <summary>Localization configuration (default locale, catalog path, supported locales).</summary>
    public ManifestI18nV3? I18n { get; init; }
    /// <summary>
    /// Settings sidebar icon (Material Design Icons class, e.g. <c>mdi-message-text-outline</c>).
    /// </summary>
    public string? Icon { get; init; }
    /// <summary>
    /// Optional plugin description shown as the settings category subtitle.
    /// </summary>
    public LocalizedNameV3? Description { get; init; }
    /// <summary>
    /// Plugin-level settings schema. Host registers these under a sidebar category named after the
    /// plugin, without starting the Node process.
    /// </summary>
    public IReadOnlyList<PluginConfigurationSettingV3> Configuration { get; init; } = [];
}

public sealed class PluginEntryV3
{
    /// <summary>Entry identity. Wire name is <c>id</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Node backend path relative to the plugin root. Wire name is <c>entry</c>.</summary>
    public required string Entry { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];
    /// <summary>
    /// Optional UI for this entry. Omitted (or <c>type: list</c>) means the host shows
    /// <c>search</c> results in the native list view. <c>type: web</c> requires <c>entry</c>.
    /// </summary>
    public EntryDetailV3? Detail { get; init; }
    /// <summary>Keyword triggers that route a search to this entry.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Default global-search participation. Omitted means not in unscoped results.</summary>
    public EntrySearchV3? Search { get; init; }
    /// <summary>Global hotkey (e.g. "Alt+S") that opens this entry (web detail or native list).</summary>
    public string? HotKey { get; init; }
    /// <summary>Display name with i18n message key + default value.</summary>
    public LocalizedNameV3? Name { get; init; }
}

/// <summary>
/// Per-entry search defaults. <c>global</c> is unscoped results; keyword search is implied by <c>keywords</c>.
/// </summary>
public sealed class EntrySearchV3
{
    public bool? Global { get; init; }
}

public sealed class EntryDetailV3
{
    /// <summary><c>web</c> (WebView2 page) or <c>list</c> (native search results). Default <c>web</c> when the object is present.</summary>
    public string Type { get; init; } = PluginDetailTypes.Web;
    public string Entry { get; init; } = "";
}

/// <summary>Allowed <c>detail.type</c> values in plugin.json.</summary>
public static class PluginDetailTypes
{
    public const string Web = "web";
    /// <summary>Native list of <c>search</c> results. Equivalent to omitting <c>detail</c>.</summary>
    public const string List = "list";
}

/// <summary>Resolved plugin UI: native list, or a WebView2 page under <see cref="Entry"/>.</summary>
public readonly record struct ResolvedPluginDetail(bool IsWeb, string? Entry);

public static class PluginDetailResolver
{
    /// <summary>
    /// Maps a plugin.json <c>detail</c> block to a UI kind.
    /// Omitted detail, empty type without entry, and <c>type: list</c> are native list.
    /// <c>type: web</c> (or omitted type with an entry path) requires <paramref name="entry"/>.
    /// </summary>
    public static ManifestValidation TryResolve(string? type, string? entry, string entryId, out ResolvedPluginDetail resolved)
    {
        resolved = new ResolvedPluginDetail(false, null);
        var trimmedType = type?.Trim();
        var hasEntry = !string.IsNullOrWhiteSpace(entry);

        if (string.Equals(trimmedType, PluginDetailTypes.List, StringComparison.OrdinalIgnoreCase))
        {
            return ManifestValidation.Ok();
        }

        if (string.IsNullOrEmpty(trimmedType) && !hasEntry)
        {
            return ManifestValidation.Ok();
        }

        if (string.IsNullOrEmpty(trimmedType)
            || string.Equals(trimmedType, PluginDetailTypes.Web, StringComparison.OrdinalIgnoreCase))
        {
            if (!hasEntry)
            {
                return ManifestValidation.Fail(
                    $"entry '{entryId}' detail.entry is required when detail.type is '{PluginDetailTypes.Web}'");
            }

            resolved = new ResolvedPluginDetail(true, entry);
            return ManifestValidation.Ok();
        }

        return ManifestValidation.Fail($"entry '{entryId}' has unsupported detail.type '{type}'");
    }
}

/// <summary>Localization block: default locale, catalog file, locales directory, supported locales.</summary>
public sealed class ManifestI18nV3
{
    public string DefaultLocale { get; init; } = "en-US";
    public string? Catalog { get; init; }
    public string? LocalesPath { get; init; }
    public IReadOnlyList<string> SupportedLocales { get; init; } = [];
}

/// <summary>A display name resolvable via i18n: a message key plus a fallback default value.</summary>
public sealed class LocalizedNameV3
{
    public required string Key { get; init; }
    public required string DefaultValue { get; init; }
}

public readonly record struct ManifestValidation(bool IsValid, BusError? Error)
{
    public static ManifestValidation Ok() => new(true, null);
    public static ManifestValidation Fail(string msg) => new(false, BusError.For(ErrorCode.InvalidPayload, msg));
}

public static class PluginManifestV3Validator
{
    public static ManifestValidation Validate(PluginManifestV3 manifest)
    {
        if (manifest.ProtocolVersion != ProtocolVersion.CurrentWire)
        {
            return ManifestValidation.Fail(
                $"unsupported protocolVersion '{manifest.ProtocolVersion}', expected {ProtocolVersion.CurrentWire}");
        }
        if (manifest.Entries is null || manifest.Entries.Count == 0)
        {
            return ManifestValidation.Fail("manifest must declare at least one entry");
        }
        var entryIds = new HashSet<string>();
        foreach (var e in manifest.Entries)
        {
            if (string.IsNullOrEmpty(e.Id))
            {
                return ManifestValidation.Fail("entry is missing id");
            }
            if (!entryIds.Add(e.Id))
            {
                return ManifestValidation.Fail($"duplicate id '{e.Id}'");
            }
            if (string.IsNullOrEmpty(e.Entry))
            {
                return ManifestValidation.Fail($"entry '{e.Id}' is missing entry");
            }

            var detailResult = PluginDetailResolver.TryResolve(e.Detail?.Type, e.Detail?.Entry, e.Id, out _);
            if (!detailResult.IsValid)
            {
                return detailResult;
            }
        }

        var configurationResult = PluginConfigurationValidator.Validate(manifest.Configuration);
        if (!configurationResult.IsValid)
        {
            return configurationResult;
        }

        return ManifestValidation.Ok();
    }
}
