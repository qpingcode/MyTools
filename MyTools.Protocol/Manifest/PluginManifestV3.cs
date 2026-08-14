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
}

public sealed class PluginEntryV3
{
    public required string EntryId { get; init; }
    public required string NodeEntry { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public EntryDetailV3? Detail { get; init; }
}

public sealed class EntryDetailV3
{
    public string Type { get; init; } = "web";
    public string Entry { get; init; } = "";
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
