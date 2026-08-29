using System.Text.Json.Nodes;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Versioning;

namespace MyTools.Protocol.Messages;

/// <summary>
/// The frozen Phase-1 message envelope carried by every transport. Field names, types and
/// semantics are frozen; see design doc §统一消息协议. New fields may only be added as optional
/// with defaults, accompanied by a minor-version bump negotiated at handshake.
/// </summary>
public sealed record class Envelope
{
    /// <summary>Negotiated protocol major.minor version (e.g. "3.0"). Handshake messages fill the sender's highest.</summary>
    public ProtocolVersion Version { get; init; }

    /// <summary>Globally-unique message id.</summary>
    public required string Id { get; init; }

    /// <summary>Points to the original request id on responses; null otherwise. Reused by Phase-2 bus.cancel.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Root request id; nested calls share one trace, standalone events use their own id.</summary>
    public required string TraceId { get; init; }

    /// <summary>Identity of this plugin run.</summary>
    public required string SessionId { get; init; }

    /// <summary>Plugin package identity.</summary>
    public required string PluginId { get; init; }

    /// <summary>Connection identity within the session.</summary>
    public required string EndpointId { get; init; }

    public required MessageKind Kind { get; init; }

    /// <summary>Constrained route name (see RouteRules).</summary>
    public required string Route { get; init; }

    /// <summary>Per-hop timeout in ms for requests; null on responses and events.</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Structured data for the route.</summary>
    public JsonNode? Payload { get; init; }

    /// <summary>Standard error object on failure responses; null otherwise.</summary>
    public BusError? Error { get; init; }
}
