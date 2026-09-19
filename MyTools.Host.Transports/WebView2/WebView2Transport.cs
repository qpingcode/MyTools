using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Framing;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Host.Transports.WebView2;

/// <summary>
/// <see cref="IMessageTransport"/> over WebView2 postMessage. Identity is fixed by
/// <see cref="EndpointBinding"/>; the page cannot switch plugin/entry/session/endpoint.
/// Outbound page messages are normalized via <see cref="WebView2Normalizer"/> (including
/// <c>host.call.*</c> → <see cref="ErrorCode.CapabilityDenied"/>). Send operations are marshalled
/// onto the UI dispatcher when one is provided. The connection is not ready for business
/// messages until the page's <c>bus.handshake</c> readiness signal succeeds.
/// </summary>
public sealed class WebView2Transport : IMessageTransport
{
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(8);

    private readonly EndpointBinding _binding;
    private readonly IWebViewMessageChannel _channel;
    private readonly WebView2Normalizer _normalizer;
    private readonly Func<Action, Task>? _dispatchAsync;
    private readonly Func<JsonObject, JsonObject>? _enrichPluginCallPayload;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _connected = true;
    private bool _handshaken;

    public WebView2Transport(
        EndpointBinding binding,
        IWebViewMessageChannel channel,
        Func<Action, Task>? dispatchAsync = null,
        Func<JsonObject, JsonObject>? enrichPluginCallPayload = null)
    {
        _binding = binding;
        _channel = channel;
        _normalizer = new WebView2Normalizer();
        _dispatchAsync = dispatchAsync;
        _enrichPluginCallPayload = enrichPluginCallPayload;
        _channel.WebMessageReceived += OnChannelMessage;
    }

    public EndpointBinding Binding => _binding;
    public WebView2Normalizer Normalizer => _normalizer;
    public bool IsHandshaken => _handshaken;
    public bool IsConnected => _connected;

    public event Action<Envelope>? MessageReceived;
    public event Action? Disconnected;
    public event Action? HandshakeSucceeded;

    public void Invalidate() => _normalizer.Invalidate();

    public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        if (!_connected)
        {
            throw new InvalidOperationException("webview transport is disconnected");
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
            _ = HandleReadinessHandshakeAsync(env);
            return;
        }

        if (!_handshaken)
        {
            if (env.Kind == MessageKind.Request)
            {
                _ = SendAsync(BuildErrorResponse(env,
                    BusError.For(ErrorCode.PluginUnavailable, "webview has not completed bus.handshake")),
                    CancellationToken.None);
            }
            return;
        }

        if (env.Kind == MessageKind.Request
            && Routes.IsPluginCall(env.Route)
            && _enrichPluginCallPayload is not null)
        {
            var payloadObj = env.Payload as JsonObject
                ?? (env.Payload is null
                    ? new JsonObject()
                    : JsonNode.Parse(env.Payload.ToJsonString()) as JsonObject ?? new JsonObject());
            env = env with { Payload = _enrichPluginCallPayload(payloadObj) };
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

    private async Task HandleReadinessHandshakeAsync(Envelope request)
    {
        _handshaken = true;
        var reply = new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = Guid.NewGuid().ToString("N"),
            CorrelationId = request.Id,
            TraceId = request.TraceId,
            Kind = MessageKind.Response,
            Route = Routes.Bus.Handshake,
        };
        await SendAsync(reply, CancellationToken.None).ConfigureAwait(false);
        HandshakeSucceeded?.Invoke();
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
        Kind = MessageKind.Response,
        Route = request.Route,
        Error = error,
    };
}
