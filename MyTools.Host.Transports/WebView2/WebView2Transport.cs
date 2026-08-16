using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Framing;
using MyTools.Protocol.Handshake;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Host.Transports.WebView2;

/// <summary>
/// <see cref="IMessageTransport"/> over WebView2 postMessage. Identity is fixed by
/// <see cref="EndpointBinding"/>; the page cannot switch plugin/entry/session/endpoint.
/// Outbound page messages are normalized via <see cref="WebView2Normalizer"/> (including
/// <c>host.call.*</c> → <see cref="ErrorCode.CapabilityDenied"/>). Send operations are marshalled
/// onto the UI dispatcher when one is provided.
///
/// Legacy <c>tool-call</c> / <c>tool-response</c> / <c>tool-event</c> are accepted and rewritten so
/// existing tests can still exercise the rewrite path. Production pages speak envelopes.
/// </summary>
public sealed class WebView2Transport : IMessageTransport
{
    public static readonly ProtocolVersion[] HostSupportedVersions = [new(3, 0)];

    private readonly EndpointBinding _binding;
    private readonly IWebViewMessageChannel _channel;
    private readonly WebView2Normalizer _normalizer;
    private readonly Func<Action, Task>? _dispatchAsync;
    private readonly Func<JsonObject, JsonObject>? _enrichDetailCallPayload;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly HashSet<string> _legacyCorrelationIds = new(StringComparer.Ordinal);
    private bool _connected = true;
    private bool _handshaken;

    public WebView2Transport(
        EndpointBinding binding,
        IWebViewMessageChannel channel,
        Func<Action, Task>? dispatchAsync = null,
        Func<JsonObject, JsonObject>? enrichDetailCallPayload = null)
    {
        _binding = binding;
        _channel = channel;
        _normalizer = new WebView2Normalizer(binding);
        _dispatchAsync = dispatchAsync;
        _enrichDetailCallPayload = enrichDetailCallPayload;
        _channel.WebMessageReceived += OnChannelMessage;
    }

    public EndpointBinding Binding => _binding;
    public WebView2Normalizer Normalizer => _normalizer;
    public bool IsHandshaken => _handshaken;
    public bool IsConnected => _connected;
    public bool LegacyShimEnabled { get; set; } = true;

    public event Action<Envelope>? MessageReceived;
    public event Action? Disconnected;

    /// <summary>
    /// Marks the transport ready without a page handshake (host-driven). Used when the page still
    /// speaks legacy <c>tool-call</c> and has not yet sent <c>bus.handshake</c>.
    /// </summary>
    public void MarkHandshaken() => _handshaken = true;

    public void Invalidate() => _normalizer.Invalidate();

    public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        if (!_connected)
        {
            throw new InvalidOperationException("webview transport is disconnected");
        }

        if (LegacyShimEnabled && TryRewriteLegacyOutbound(envelope, out var legacyJson))
        {
            await PostRawAsync(legacyJson, cancellationToken).ConfigureAwait(false);
            return;
        }

        var json = JsonSerializer.Serialize(envelope, ProtocolJsonOptions.Default);
        await PostRawAsync(json, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_connected) return;
        _connected = false;
        _channel.WebMessageReceived -= OnChannelMessage;
        Disconnected?.Invoke();
        await ValueTask.CompletedTask;
    }

    private void OnChannelMessage(string json)
    {
        if (!_connected) return;

        if (Encoding.UTF8.GetByteCount(json) > FrameLimits.MaxFrameBytes)
        {
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            _ = DisposeAsync();
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (LegacyShimEnabled
                && root.TryGetProperty("type", out var typeEl)
                && string.Equals(typeEl.GetString(), "tool-call", StringComparison.OrdinalIgnoreCase))
            {
                HandleLegacyToolCall(root);
                return;
            }

            // Host-side subscribe messages stay local to the page client; ignore at transport.
            if (root.TryGetProperty("type", out var t)
                && t.GetString() is "tool-subscribe" or "tool-unsubscribe" or "ready")
            {
                return;
            }

            Envelope? env;
            try
            {
                env = JsonSerializer.Deserialize<Envelope>(json, ProtocolJsonOptions.Default);
            }
            catch
            {
                _ = DisposeAsync();
                return;
            }

            if (env is null || string.IsNullOrEmpty(env.Route))
            {
                return;
            }

            if (Routes.IsHandshake(env.Route) && env.Kind == MessageKind.Request)
            {
                _ = HandleHandshakeAsync(env);
                return;
            }

            if (!_handshaken)
            {
                return;
            }

            if (env.Kind == MessageKind.Request
                && env.Route.Equals(Routes.PluginCall.DetailCall, StringComparison.Ordinal)
                && _enrichDetailCallPayload is not null)
            {
                var payloadObj = env.Payload as JsonObject
                    ?? (env.Payload is null
                        ? new JsonObject()
                        : JsonNode.Parse(env.Payload.ToJsonString()) as JsonObject ?? new JsonObject());
                env = env with { Payload = _enrichDetailCallPayload(payloadObj) };
            }

            var result = _normalizer.NormalizeOutbound(env, FrameLimits.MaxFrameBytes);
            if (result.IsRejected)
            {
                if (env.Kind == MessageKind.Request)
                {
                    _ = SendAsync(BuildErrorResponse(env, result.Error!), CancellationToken.None);
                }
                return;
            }

            MessageReceived?.Invoke(result.Envelope!);
        }
    }

    private void HandleLegacyToolCall(JsonElement root)
    {
        if (!_handshaken) MarkHandshaken();

        var requestId = root.TryGetProperty("requestId", out var idEl) ? idEl.GetString() : null;
        var action = root.TryGetProperty("action", out var actionEl) ? actionEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        JsonNode? userPayload = null;
        if (root.TryGetProperty("payload", out var payloadEl)
            && payloadEl.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            userPayload = JsonNode.Parse(payloadEl.GetRawText());
        }

        var detailPayload = new JsonObject
        {
            ["action"] = action,
            ["payload"] = userPayload,
        };
        if (_enrichDetailCallPayload is not null)
        {
            detailPayload = _enrichDetailCallPayload(detailPayload);
        }

        var env = new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = requestId,
            TraceId = requestId,
            SessionId = _binding.SessionId,
            PluginId = _binding.PluginId,
            EntryId = _binding.EntryId,
            EndpointId = _binding.EndpointId,
            Kind = MessageKind.Request,
            Route = Routes.PluginCall.DetailCall,
            TimeoutMs = 30_000,
            Payload = detailPayload,
        };

        var result = _normalizer.NormalizeOutbound(env, FrameLimits.MaxFrameBytes);
        if (result.IsRejected)
        {
            _ = PostRawAsync(JsonSerializer.Serialize(new
            {
                type = "tool-response",
                requestId,
                ok = false,
                payload = new { },
                error = new { message = result.Error!.Message, code = result.Error.Code.ToString() },
            }), CancellationToken.None);
            return;
        }

        _legacyCorrelationIds.Add(requestId);
        MessageReceived?.Invoke(result.Envelope!);
    }

    private async Task HandleHandshakeAsync(Envelope request)
    {
        HandshakePayload? payload = null;
        try
        {
            if (request.Payload is not null)
            {
                payload = request.Payload.Deserialize<HandshakePayload>(ProtocolJsonOptions.Default);
            }
        }
        catch
        {
            payload = null;
        }

        var theirs = payload?.SupportedVersions
            ?? (payload?.Version is { } v ? new[] { v } : Array.Empty<ProtocolVersion>());
        var negotiated = HandshakeNegotiator.Negotiate(HostSupportedVersions, theirs);
        Envelope reply;
        if (!negotiated.IsSuccess)
        {
            reply = BuildErrorResponse(request, negotiated.Error!);
        }
        else
        {
            _handshaken = true;
            var version = negotiated.Negotiated
                ?? throw new InvalidOperationException("handshake succeeded without negotiated version");
            var successPayload = HandshakePayload.BuildSuccessResponse(
                version,
                _binding.PluginId,
                _binding.EntryId,
                _binding.SessionId,
                _binding.EndpointId);
            reply = new Envelope
            {
                Version = ProtocolVersion.Current,
                Id = Guid.NewGuid().ToString("N"),
                CorrelationId = request.Id,
                TraceId = request.TraceId,
                SessionId = _binding.SessionId,
                PluginId = _binding.PluginId,
                EntryId = _binding.EntryId,
                EndpointId = _binding.EndpointId,
                Kind = MessageKind.Response,
                Route = Routes.Bus.Handshake,
                Payload = JsonSerializer.SerializeToNode(successPayload, ProtocolJsonOptions.Default),
            };
        }

        await SendAsync(reply, CancellationToken.None).ConfigureAwait(false);
    }

    private bool TryRewriteLegacyOutbound(Envelope envelope, out string json)
    {
        if (envelope.Kind == MessageKind.Response
            && envelope.CorrelationId is { } corr
            && _legacyCorrelationIds.Remove(corr))
        {
            object? payload = envelope.Payload;
            if (envelope.Payload is JsonObject obj && obj.TryGetPropertyValue("result", out var resultNode))
            {
                payload = resultNode;
            }

            json = JsonSerializer.Serialize(new
            {
                type = "tool-response",
                requestId = corr,
                ok = envelope.Error is null,
                payload = payload ?? new { },
                error = envelope.Error is null
                    ? null
                    : new { message = envelope.Error.Message, code = envelope.Error.Code.ToString() },
            }, ProtocolJsonOptions.Default);
            return true;
        }

        if (envelope.Kind == MessageKind.Event)
        {
            json = JsonSerializer.Serialize(new
            {
                type = "tool-event",
                subjectId = envelope.Route,
                payload = (object?)envelope.Payload ?? new { },
            }, ProtocolJsonOptions.Default);
            return true;
        }

        json = "";
        return false;
    }

    private async ValueTask PostRawAsync(string json, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_dispatchAsync is not null)
            {
                await _dispatchAsync(() => _channel.PostWebMessageAsJson(json)).ConfigureAwait(false);
            }
            else
            {
                _channel.PostWebMessageAsJson(json);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private Envelope BuildErrorResponse(Envelope request, BusError error) => new()
    {
        Version = ProtocolVersion.Current,
        Id = Guid.NewGuid().ToString("N"),
        CorrelationId = request.Id,
        TraceId = request.TraceId,
        SessionId = _binding.SessionId,
        PluginId = _binding.PluginId,
        EntryId = _binding.EntryId,
        EndpointId = _binding.EndpointId,
        Kind = MessageKind.Response,
        Route = request.Route,
        Error = error,
    };
}
