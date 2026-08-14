using System.Collections.Generic;
using MyTools.Protocol.Errors;

namespace MyTools.Host.Core.Capabilities;

/// <summary>
/// Declared capabilities for a plugin entry (Phase-1: declaration = grant). The manifest reflects
/// the true capability surface; undeclared calls are rejected even though plugins are trusted.
/// </summary>
public sealed record PluginManifest(string PluginId, string EntryId, IReadOnlyList<string> Capabilities);

public readonly record struct CapabilityDecision(bool IsAllowed, BusError? Error)
{
    public static CapabilityDecision Allow() => new(true, null);
    public static CapabilityDecision Deny(BusError e) => new(false, e);
}

/// <summary>One audited capability invocation (who, what route, result).</summary>
public sealed record CapabilityAuditEntry(string PluginId, string EntryId, string Route, bool Allowed);

/// <summary>
/// Phase-1 capability gateway skeleton. Architecture position and per-call validation match the
/// final form so that Phase-3 authorization can be plugged in without moving components. In Phase-1
/// the authorization decision is "declared ⇒ granted"; Phase-3 replaces that single decision point.
/// </summary>
public sealed class CapabilityGateway
{
    private readonly Dictionary<string, PluginManifest> _manifests = new();
    private readonly List<CapabilityAuditEntry> _audit = new();

    public IReadOnlyList<CapabilityAuditEntry> AuditEntries => _audit;

    public void RegisterManifest(PluginManifest manifest)
        => _manifests[Key(manifest.PluginId, manifest.EntryId)] = manifest;

    public void UnregisterManifest(string pluginId, string entryId)
        => _manifests.Remove(Key(pluginId, entryId));

    public CapabilityDecision Authorize(string pluginId, string entryId, string capabilityRoute)
    {
        var key = Key(pluginId, entryId);
        var allowed = _manifests.TryGetValue(key, out var manifest)
                      && manifest!.Capabilities.Contains(capabilityRoute);

        _audit.Add(new CapabilityAuditEntry(pluginId, entryId, capabilityRoute, allowed));

        return allowed
            ? CapabilityDecision.Allow()
            : CapabilityDecision.Deny(BusError.For(ErrorCode.CapabilityNotDeclared,
                $"capability '{capabilityRoute}' is not declared by {pluginId}/{entryId}"));
    }

    private static string Key(string pluginId, string entryId) => $"{pluginId}\u001f{entryId}";
}
