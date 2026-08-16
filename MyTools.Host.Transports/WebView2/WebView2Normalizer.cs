using System;
using System.Text.Json;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Framing;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;

namespace MyTools.Host.Transports.WebView2;

/// <summary>
/// The fixed identity bound to a WebView2 transport at creation. The page cannot declare or switch
/// any of these; the host stamps them on every outbound message.
/// </summary>
public sealed record EndpointBinding(
    string PluginId, string EntryId, string SessionId, string EndpointId);

/// <summary>Result of normalizing an outbound WebView message.</summary>
public readonly record struct NormalizationResult(bool IsRejected, BusError? Error, Envelope? Envelope)
{
    public static NormalizationResult Ok(Envelope e) => new(false, null, e);
    public static NormalizationResult Reject(BusError e) => new(true, e, null);
}

/// <summary>
/// Normalizes outbound WebView2 messages at the transport boundary, enforcing the design's
/// WebView rules: (1) the host stamps pluginId/entryId/sessionId/endpointId from the fixed binding,
/// ignoring whatever the page declared; (2) webview may only call <c>plugin.call.*</c> or publish
/// <c>plugin.event.*</c> — a <c>host.call.*</c> is rejected with <see cref="ErrorCode.CapabilityDenied">;
/// (3) message byte-size is prechecked before deserialization would matter; (4) on Node restart the
/// binding is invalidated and the old page's messages are rejected (<c>PluginUnavailable</c>).
/// </summary>
public sealed class WebView2Normalizer
{
    private readonly EndpointBinding _binding;
    private bool _valid = true;

    /// <summary>
    /// Baseline Content-Security-Policy injected into the per-plugin virtual origin. Phase 1 uses a
    /// conservative default; Phase 3 upgrades this to an unwaivable policy check.
    /// </summary>
    public const string BaselineCsp =
        "default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'none'";

    public WebView2Normalizer(EndpointBinding binding) => _binding = binding;

    /// <summary>Marks this binding as stale (Node restarted). Subsequent messages are rejected.</summary>
    public void Invalidate() => _valid = false;

    public NormalizationResult NormalizeOutbound(Envelope env, int? maxBytes = null)
    {
        if (!_valid)
        {
            return NormalizationResult.Reject(
                BusError.For(ErrorCode.PluginUnavailable, "endpoint invalidated by Node reload"));
        }

        // Route gate: webview cannot call host capabilities directly.
        var route = env.Route ?? "";
        if (Routes.IsHostCall(route))
        {
            return NormalizationResult.Reject(BusError.For(ErrorCode.CapabilityDenied,
                "webview cannot call host.call.* directly; route through plugin.call.* to Node"));
        }

        // Stamp the bound identity over whatever the page declared.
        var stamped = env with
        {
            PluginId = _binding.PluginId,
            EntryId = _binding.EntryId,
            SessionId = _binding.SessionId,
            EndpointId = _binding.EndpointId,
        };

        // Byte-size precheck (simulates pre-deserialization byte-length check on the wire frame).
        if (maxBytes is { } cap)
        {
            var serialized = JsonSerializer.SerializeToUtf8Bytes(stamped, ProtocolJsonOptions.Default);
            if (serialized.Length > cap)
            {
                return NormalizationResult.Reject(BusError.For(ErrorCode.MessageTooLarge,
                    $"message of {serialized.Length} bytes exceeds cap of {cap}"));
            }
        }

        return NormalizationResult.Ok(stamped);
    }
}
