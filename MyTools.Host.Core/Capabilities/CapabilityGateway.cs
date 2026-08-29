using System.Collections.Concurrent;
using System.Collections.Generic;
using MyTools.Protocol.Errors;

namespace MyTools.Host.Core.Capabilities;

/// <summary>
/// Declared capabilities for a plugin (Phase-1: declaration = grant). The manifest reflects
/// the true capability surface; undeclared calls are rejected even though plugins are trusted.
/// </summary>
public sealed record PluginManifest(string PluginId, IReadOnlyList<string> Capabilities);

public readonly record struct CapabilityDecision(bool IsAllowed, BusError? Error)
{
    public static CapabilityDecision Allow() => new(true, null);
    public static CapabilityDecision Deny(BusError e) => new(false, e);
}

/// <summary>One audited capability invocation (who, what route, result).</summary>
public sealed record CapabilityAuditEntry(string PluginId, string Route, bool Allowed);

/// <summary>
/// Phase-1 capability gateway skeleton. Architecture position and per-call validation match the
/// final form so that Phase-3 authorization can be plugged in without moving components. In Phase-1
/// the authorization decision is "declared ⇒ granted"; Phase-3 replaces that single decision point.
/// Session start, host.call, and teardown all hit this type from different threads.
/// </summary>
public sealed class CapabilityGateway
{
    private readonly ConcurrentDictionary<string, PluginManifest> _manifests = new();
    private readonly ConcurrentQueue<CapabilityAuditEntry> _audit = new();

    public IReadOnlyList<CapabilityAuditEntry> AuditEntries => _audit.ToArray();

    public void RegisterManifest(PluginManifest manifest)
        => _manifests[manifest.PluginId] = manifest;

    public void UnregisterManifest(string pluginId)
        => _manifests.TryRemove(pluginId, out _);

    public CapabilityDecision Authorize(string pluginId, string capabilityRoute)
    {
        var allowed = _manifests.TryGetValue(pluginId, out var manifest)
                      && manifest.Capabilities.Contains(capabilityRoute);

        _audit.Enqueue(new CapabilityAuditEntry(pluginId, capabilityRoute, allowed));

        return allowed
            ? CapabilityDecision.Allow()
            : CapabilityDecision.Deny(BusError.For(ErrorCode.CapabilityNotDeclared,
                $"capability '{capabilityRoute}' is not declared by {pluginId}"));
    }
}
