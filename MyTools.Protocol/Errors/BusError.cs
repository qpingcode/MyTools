using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MyTools.Protocol.Errors;

/// <summary>
/// Stable machine-readable error codes. The 12 active codes are produced in Phase 1;
/// <see cref="Cancelled"/> and <see cref="RateLimited"/> are reserved for later phases
/// (defined but never produced in Phase 1).
/// </summary>
[JsonConverter(typeof(ErrorCodeConverter))]
public enum ErrorCode
{
    ProtocolMismatch,
    HandshakeFailed,
    CapabilityNotDeclared,
    CapabilityDenied,
    InvalidPayload,
    MessageTooLarge,
    RouteNotFound,
    RequestTimeout,
    TooManyRequests,
    TransportDisconnected,
    PluginUnavailable,
    InternalError,
    // Reserved for Phase 2/3 — defined but not produced in Phase 1.
    Cancelled,
    RateLimited,
}

/// <summary>
/// The standard error object carried by a failure response's <c>error</c> field.
/// <c>details</c> must not contain credentials or full sensitive payloads.
/// </summary>
public sealed record BusError
{
    // Parameterless ctor enables the `new() { ... }` initializer used by For(...).
    public BusError() { }

    public required ErrorCode Code { get; init; }
    public required string Message { get; init; }
    public bool Retryable { get; init; }
    public JsonNode? Details { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public BusError(ErrorCode code, string message, bool retryable, object? details)
        => (Code, Message, Retryable, Details) = (code, message, retryable,
            details is null
                ? null
                : JsonNode.Parse(JsonSerializer.Serialize(details, ProtocolJsonOptions.Default)));

    /// <summary>Convenience factory with <c>Retryable = false</c> and no details.</summary>
    public static BusError For(ErrorCode code, string? message = null, bool retryable = false, object? details = null)
        => new()
        {
            Code = code,
            Message = message ?? code.ToString(),
            Retryable = retryable,
            Details = details is null
                ? null
                : JsonNode.Parse(JsonSerializer.Serialize(details, ProtocolJsonOptions.Default))
        };
}

/// <summary>
/// Central, frozen <see cref="JsonSerializerOptions"/> for all protocol serialization:
/// camelCase property naming, case-insensitive reading, indented disabled.
/// All envelope/error/handshake code paths must use this to guarantee wire-format stability.
/// </summary>
public static class ProtocolJsonOptions
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

file sealed class ErrorCodeConverter : JsonConverter<ErrorCode>
{
    public override ErrorCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return Enum.TryParse<ErrorCode>(s, ignoreCase: false, out var code)
            ? code
            : throw new JsonException($"unknown error code '{s}'");
    }

    public override void Write(Utf8JsonWriter writer, ErrorCode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
