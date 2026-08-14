using MyTools.Protocol.Errors;

namespace MyTools.Protocol.Messages;

/// <summary>
/// Result of validating an inbound envelope's structure (not its route — see RouteRules).
/// </summary>
public readonly record struct EnvelopeValidationResult(bool IsValid, BusError? Error)
{
    public static EnvelopeValidationResult Ok() => new(true, null);
    public static EnvelopeValidationResult Fail(string field)
        => new(false, BusError.For(ErrorCode.InvalidPayload, $"missing or invalid field '{field}'"));
}

/// <summary>
/// Validates the envelope structure required before a message enters the bus:
/// required fields present, kind/route consistency rules. Route legality is checked by RouteRules.
/// This is structural validation (防 bug), not authorization (防恶意 — Phase 3).
/// </summary>
public static class EnvelopeValidator
{
    public static EnvelopeValidationResult Validate(Envelope env)
    {
        if (string.IsNullOrEmpty(env.Id)) return EnvelopeValidationResult.Fail("id");
        if (string.IsNullOrEmpty(env.TraceId)) return EnvelopeValidationResult.Fail("traceId");
        if (string.IsNullOrEmpty(env.SessionId)) return EnvelopeValidationResult.Fail("sessionId");
        if (string.IsNullOrEmpty(env.PluginId)) return EnvelopeValidationResult.Fail("pluginId");
        if (string.IsNullOrEmpty(env.EntryId)) return EnvelopeValidationResult.Fail("entryId");
        if (string.IsNullOrEmpty(env.EndpointId)) return EnvelopeValidationResult.Fail("endpointId");
        if (string.IsNullOrEmpty(env.Route)) return EnvelopeValidationResult.Fail("route");

        // Responses must carry correlationId pointing to the original request.
        if (env.Kind == MessageKind.Response && string.IsNullOrEmpty(env.CorrelationId))
        {
            return EnvelopeValidationResult.Fail("correlationId");
        }

        // Only responses may carry an error.
        if (env.Kind != MessageKind.Response && env.Error is not null)
        {
            return EnvelopeValidationResult.Fail("error");
        }

        // Only requests carry a timeout.
        if (env.Kind != MessageKind.Request && env.TimeoutMs is not null)
        {
            return EnvelopeValidationResult.Fail("timeoutMs");
        }

        return EnvelopeValidationResult.Ok();
    }
}
