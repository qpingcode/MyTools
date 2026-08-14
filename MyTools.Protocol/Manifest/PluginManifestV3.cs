using System.Collections.Generic;
using System.Linq;
using MyTools.Protocol.Errors;

namespace MyTools.Protocol.Manifest;

/// <summary>
/// v3 plugin manifest. The key Phase-1 addition over v2 is that each entry declares its required
/// capabilities explicitly; the capability gateway rejects undeclared calls even for trusted plugins.
/// </summary>
public sealed class PluginManifestV3
{
    public required string Id { get; init; }
    public string Version { get; init; } = "0.0.0";
    public required string ProtocolVersion { get; init; }
    public required IReadOnlyList<PluginEntryV3> Entries { get; init; }
    /// <summary>Localization configuration (default locale, catalog path, supported locales).</summary>
    public ManifestI18nV3? I18n { get; init; }
}

public sealed class PluginEntryV3
{
    public required string EntryId { get; init; }
    public required string NodeEntry { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public EntryDetailV3? Detail { get; init; }
    /// <summary>Keyword triggers that route a search to this entry.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Global hotkey (e.g. "Alt+S") that opens this entry's detail view.</summary>
    public string? HotKey { get; init; }
    /// <summary>Display name with i18n message key + default value.</summary>
    public LocalizedNameV3? Name { get; init; }
}

public sealed class EntryDetailV3
{
    public string Type { get; init; } = "web";
    public string Entry { get; init; } = "";
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
        if (manifest.ProtocolVersion != "3.0")
        {
            return ManifestValidation.Fail($"unsupported protocolVersion '{manifest.ProtocolVersion}', expected 3.0");
        }
        if (manifest.Entries is null || manifest.Entries.Count == 0)
        {
            return ManifestValidation.Fail("manifest must declare at least one entry");
        }
        var entryIds = new HashSet<string>();
        foreach (var e in manifest.Entries)
        {
            if (string.IsNullOrEmpty(e.EntryId))
            {
                return ManifestValidation.Fail("entry is missing entryId");
            }
            if (!entryIds.Add(e.EntryId))
            {
                return ManifestValidation.Fail($"duplicate entryId '{e.EntryId}'");
            }
            if (string.IsNullOrEmpty(e.NodeEntry))
            {
                return ManifestValidation.Fail($"entry '{e.EntryId}' is missing nodeEntry");
            }
        }
        return ManifestValidation.Ok();
    }
}
