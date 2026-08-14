using System.Collections.Generic;
using System.Linq;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Versioning;

namespace MyTools.Protocol.Handshake;

/// <summary>
/// Result of version negotiation.
/// </summary>
public readonly record struct HandshakeResult(bool IsSuccess, ProtocolVersion? Negotiated, BusError? Error)
{
    public static HandshakeResult Success(ProtocolVersion v) => new(true, v, null);
    public static HandshakeResult Fail(BusError e) => new(false, null, e);
}

/// <summary>
/// Handshake payload model. Envelope fields used by handshake are permanently frozen
/// (<c>version</c>, <c>id</c>, <c>correlationId</c>, <c>kind</c>, <c>route</c>, <c>payload</c>,
/// <c>error</c>). Optional fields may be added inside the payload: named-pipe handshakes carry a
/// one-shot <see cref="Token"/> on the request and bind identity on the success response.
/// </summary>
public sealed class HandshakePayload
{
    /// <summary>Sender's highest supported version (filled in the request).</summary>
    public ProtocolVersion? Version { get; init; }

    /// <summary>All major.minor versions the sender supports.</summary>
    public IReadOnlyList<ProtocolVersion>? SupportedVersions { get; init; }

    /// <summary>The version agreed upon (filled in a success response).</summary>
    public ProtocolVersion? NegotiatedVersion { get; init; }

    /// <summary>One-shot bootstrap token presented by the Node side (named-pipe only).</summary>
    public string? Token { get; init; }

    /// <summary>Bound plugin package id returned to Node on success (for SDK identity stamping).</summary>
    public string? PluginId { get; init; }

    /// <summary>Bound entry id returned to Node on success.</summary>
    public string? EntryId { get; init; }

    /// <summary>Bound session id returned to Node on success.</summary>
    public string? SessionId { get; init; }

    /// <summary>Bound endpoint id returned to Node on success (e.g. <c>node-main</c>).</summary>
    public string? EndpointId { get; init; }

    public static HandshakePayload BuildRequest(IEnumerable<ProtocolVersion> supported)
    {
        var list = supported.ToArray();
        return new HandshakePayload
        {
            Version = list.MaxBy(v => v),
            SupportedVersions = list,
        };
    }

    public static HandshakePayload BuildNamedPipeRequest(IEnumerable<ProtocolVersion> supported, string token)
    {
        var list = supported.ToArray();
        return new HandshakePayload
        {
            Version = list.MaxBy(v => v),
            SupportedVersions = list,
            Token = token,
        };
    }

    public static HandshakePayload BuildSuccessResponse(ProtocolVersion negotiated)
        => new() { NegotiatedVersion = negotiated };

    public static HandshakePayload BuildSuccessResponse(
        ProtocolVersion negotiated,
        string pluginId,
        string entryId,
        string sessionId,
        string endpointId)
        => new()
        {
            NegotiatedVersion = negotiated,
            PluginId = pluginId,
            EntryId = entryId,
            SessionId = sessionId,
            EndpointId = endpointId,
        };
}

/// <summary>
/// Negotiates the protocol version at handshake. Major mismatch or no common minor is fatal
/// (ProtocolMismatch); otherwise the highest common minor is chosen.
/// </summary>
public static class HandshakeNegotiator
{
    public static HandshakeResult Negotiate(IEnumerable<ProtocolVersion> ours, IEnumerable<ProtocolVersion> theirs)
    {
        var ourList = ours as IReadOnlyList<ProtocolVersion> ?? ours.ToArray();
        var theirList = theirs as IReadOnlyList<ProtocolVersion> ?? theirs.ToArray();

        var ourMajor = ourList.MaxBy(v => v).Major;
        var theirMajor = theirList.MaxBy(v => v).Major;

        if (ourMajor != theirMajor)
        {
            return HandshakeResult.Fail(BusError.For(ErrorCode.ProtocolMismatch,
                $"major version mismatch: host {ourMajor}.x vs peer {theirMajor}.x"));
        }

        // Find the highest minor both sides support at the common major.
        var best = -1;
        foreach (var o in ourList)
        {
            foreach (var t in theirList)
            {
                if (o.Major == ourMajor && o == t && o.Minor > best)
                {
                    best = o.Minor;
                }
            }
        }

        return best >= 0
            ? HandshakeResult.Success(new ProtocolVersion(ourMajor, best))
            : HandshakeResult.Fail(BusError.For(ErrorCode.ProtocolMismatch,
                $"no common minor version at major {ourMajor}"));
    }
}
