using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Manifest;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// v3 message-bus runtime for a Node plugin entry, implementing the same <see cref="INodePluginHost"/>
/// surface as the legacy <see cref="NodePluginProcessHost"/> but over the v3 named-pipe bus. Each
/// legacy method (<c>search</c>, <c>detailCall</c>, …) is mapped to a <c>plugin.call.&lt;method&gt;</c>
/// envelope; responses are correlated by request id. Inbound <c>plugin.event.*</c> envelopes raise
/// <see cref="EventReceived"/>; inbound <c>host.call.*</c> requests are dispatched to
/// <see cref="HostCallHandler"/>. The Node process is owned by the <see cref="PluginSessionManager"/>.
/// </summary>
internal sealed class NodePluginBusHost : INodePluginHost
{
    private readonly NodePluginManifest _manifest;
    private readonly PluginSessionManager _sessionManager;
    private readonly MessageBus _bus;
    private readonly ILogger<NodePluginBusHost> _logger;
    private readonly IIdGenerator _ids;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly SemaphoreSlim _startLock = new(1, 1);

    private PluginSession? _session;
    private EndpointId? _nodeEndpoint;
    private int _started; // 0 = not started, 1 = started

    /// <summary>Default per-request timeout (ms) when the caller does not supply a cancellation token.</summary>
    private const int DefaultTimeoutMs = 30000;

    /// <summary>The node executable path used to spawn the child process. Override via constructor if needed.</summary>
    public string NodeExePath { get; set; } = "node";

    public PluginSession? Session => _session;

    public NodePluginBusHost(NodePluginManifest manifest, PluginSessionManager sessionManager,
        MessageBus bus, ILogger<NodePluginBusHost> logger, IIdGenerator? ids = null)
    {
        _manifest = manifest;
        _sessionManager = sessionManager;
        _bus = bus;
        _logger = logger;
        _ids = ids ?? new GuidIdGenerator();
        _logger.LogInformation("NodePluginBusHost created for plugin={PluginId} entry={EntryId}", manifest.ParentId, manifest.EntryId);
    }

    public event EventHandler<NodePluginEventReceivedEventArgs>? EventReceived;

    public Func<HostCallRequest, CancellationToken, Task<JsonElement>>? HostCallHandler { get; set; }

    /// <summary>Starts the session explicitly: spawns the Node process and connects the bus.</summary>
    public async Task StartAsync(string nodeExePath, CancellationToken cancellationToken)
    {
        NodeExePath = nodeExePath;
        await EnsureStartedAsync(cancellationToken);
    }

    /// <summary>
    /// Lazy start (double-checked lock): spawns the Node process and connects the pipe on first use.
    /// Mirrors NodePluginProcessHost.EnsureStartedAsync so NodePlugin can use the bus host without
    /// any explicit start call — the first SearchAsync/InitializeAsync/etc. triggers it.
    /// </summary>
    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (System.Threading.Volatile.Read(ref _started) == 1) return;

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (_started == 1) return;

            var v3 = ToV3Manifest();
            _session = await _sessionManager.StartSessionAsync(v3, _manifest.EntryId, NodeExePath, cancellationToken);
            _nodeEndpoint = new EndpointId(_manifest.ParentId, _manifest.EntryId, _session.SessionId,
                "node-main", IsNode: true);

            // Listen for inbound envelopes from the Node side.
            var nodeTransport = _session.Controller?.Transport;
            if (nodeTransport is not null)
            {
                nodeTransport.MessageReceived += OnInbound;
            }

            System.Threading.Volatile.Write(ref _started, 1);
        }
        finally
        {
            _startLock.Release();
        }
    }

    private void OnInbound(Envelope env)
    {
        _logger.LogInformation("OnInbound: kind={Kind} route={Route} corr={CorrelationId}", env.Kind, env.Route, env.CorrelationId);
        switch (env.Kind)
        {
            case MessageKind.Response:
                HandleResponse(env);
                break;
            case MessageKind.Event:
                HandleEvent(env);
                break;
            case MessageKind.Request:
                _ = HandleHostCallAsync(env);
                break;
        }
    }

    public Task<JsonElement> InitializeAsync(string locale, string fallbackLocale,
        IReadOnlyDictionary<string, string> messages, CancellationToken cancellationToken = default)
    {
        return SendAndUnwrapResultAsync("plugin.call.initialize", new
        {
            locale, fallbackLocale, messages,
        }, cancellationToken);
    }

    public Task<NodePluginSearchResponse> SearchAsync(string query, string mode, string locale,
        string fallbackLocale, CancellationToken cancellationToken)
    {
        return SendAsync<NodePluginSearchResponse>("plugin.call.search", new
        {
            query, mode, locale, fallbackLocale,
        }, cancellationToken);
    }

    public Task<NodePluginActionResponse> InvokeActionAsync(string itemId, string actionId, string query,
        string locale, string fallbackLocale, CancellationToken cancellationToken = default)
    {
        return SendAsync<NodePluginActionResponse>("plugin.call.invokeAction", new
        {
            itemId, actionId, query, locale, fallbackLocale,
        }, cancellationToken);
    }

    public Task<NodePluginDetailEventResponse> SendDetailEventAsync(string itemId, string eventName,
        JsonElement? payload, string query, string locale, string fallbackLocale,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NodePluginDetailEventResponse>("plugin.call.detailEvent", new
        {
            itemId, eventName, query, payload, locale, fallbackLocale,
        }, cancellationToken);
    }

    public Task<NodePluginDetailCallResponse> SendDetailCallAsync(string itemId, string action,
        JsonElement? payload, string query, string locale, string fallbackLocale,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NodePluginDetailCallResponse>("plugin.call.detailCall", new
        {
            itemId, action, query, payload, locale, fallbackLocale,
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_session is not null)
        {
            _ = _sessionManager.StopSessionAsync(_manifest.ParentId, _manifest.EntryId, _session.SessionId);
        }
    }

    // --- helpers ---

    private async Task<T> SendAsync<T>(string route, object parameters, CancellationToken cancellationToken)
    {
        var payloadJson = await SendAndAwaitResponseAsync(route, parameters, cancellationToken);
        var result = payloadJson is null
            ? default
            : JsonSerializer.Deserialize<T>(payloadJson.ToJsonString(), ProtocolJsonOptions.Default);
        return result ?? (typeof(T) == typeof(string) ? (T)(object)string.Empty : default!);
    }

    private async Task<JsonElement> SendAndUnwrapResultAsync(string route, object parameters,
        CancellationToken cancellationToken)
    {
        var payloadJson = await SendAndAwaitResponseAsync(route, parameters, cancellationToken);
        if (payloadJson is null) return default;
        return JsonDocument.Parse(payloadJson.ToJsonString()).RootElement.Clone();
    }

    private async Task<JsonNode?> SendAndAwaitResponseAsync(string route, object parameters,
        CancellationToken cancellationToken)
    {
        // Lazy start: spawn the Node process on first use (mirrors v2 NodePluginProcessHost).
        await EnsureStartedAsync(cancellationToken);

        if (_session is null || _nodeEndpoint is null)
        {
            throw new System.InvalidOperationException("bus host failed to start");
        }

        var id = _ids.NewId();
        var env = new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = id,
            TraceId = id,
            SessionId = _session.SessionId,
            PluginId = _manifest.ParentId,
            EntryId = _manifest.EntryId,
            EndpointId = "host",
            Kind = MessageKind.Request,
            Route = route,
            TimeoutMs = DefaultTimeoutMs,
            Payload = JsonNode.Parse(JsonSerializer.Serialize(parameters, ProtocolJsonOptions.Default)),
        };

        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        // Real per-request timeout: a linked CTS that fires at DefaultTimeoutMs OR when the caller
        // cancels. On timeout the pending slot is removed and the task faults with TimeoutException.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeoutMs);
        var requestStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _logger.LogInformation($"id: {id}, start");
        using var _ = timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var timedOutTcs))
            {
                _logger.LogInformation($"id: {id}, cancelled");
                var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(requestStartedAt).TotalMilliseconds;
                _logger.LogWarning("plugin.call '{Route}' timed out after {ElapsedMs}ms (expected {TimeoutMs}ms)", route, elapsedMs, DefaultTimeoutMs);
                timedOutTcs.TrySetCanceled();
            }
        });

        // Route the request to the Node endpoint. Use a synthetic "host" origin (not a real
        // endpoint) so the bus can find the session; the response returns via OnInbound.
        var hostOrigin = new EndpointId(_manifest.ParentId, _manifest.EntryId, _session.SessionId,
            "host", IsNode: false);
        await _bus.RouteRequestAsync(env, hostOrigin);
        _logger.LogInformation("Sent plugin.call '{Route}' id={Id}, waiting for response", route, id);

        return await tcs.Task;
    }

    private void HandleResponse(Envelope env)
    {
        if (env.CorrelationId is null) return;
        if (!_pending.TryRemove(env.CorrelationId, out var tcs)) return;

        if (env.Error is not null)
        {
            tcs.TrySetException(new BusCallException(env.Error.Code.ToString(), env.Error.Message));
        }
        else
        {
            tcs.TrySetResult(env.Payload);
        }
    }

    private void HandleEvent(Envelope env)
    {
        var payload = env.Payload?.ToJsonString() ?? "null";
        EventReceived?.Invoke(this, new NodePluginEventReceivedEventArgs
        {
            SubjectId = env.Route,
            Payload = JsonDocument.Parse(payload).RootElement.Clone(),
        });
    }

    private async Task HandleHostCallAsync(Envelope env)
    {
        var transport = _session?.Controller?.Transport;
        if (transport is null)
        {
            _logger.LogWarning("host call {Route} dropped: no node transport", env.Route);
            return;
        }

        Envelope reply;
        try
        {
            if (HostCallHandler is null)
            {
                throw new System.InvalidOperationException("No host call handler registered for this plugin.");
            }

            var req = new HostCallRequest(env.Route.Replace("host.call.", ""),
                JsonDocument.Parse(env.Payload?.ToJsonString() ?? "{}").RootElement.Clone());
            var result = await HostCallHandler(req, CancellationToken.None);
            reply = BuildHostCallReply(env, payload: JsonNode.Parse(result.GetRawText()), error: null);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "host call {Route} failed", env.Route);
            reply = BuildHostCallReply(env, payload: null,
                error: BusError.For(ErrorCode.InternalError, ex.Message));
        }

        try
        {
            await transport.SendAsync(reply, CancellationToken.None);
            _logger.LogInformation("Replied to host.call '{Route}' corr={CorrelationId}", env.Route, env.Id);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to send host.call reply for {Route}", env.Route);
        }
    }

    private Envelope BuildHostCallReply(Envelope request, JsonNode? payload, BusError? error)
    {
        return new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = _ids.NewId(),
            CorrelationId = request.Id,
            TraceId = request.TraceId,
            SessionId = request.SessionId,
            PluginId = request.PluginId,
            EntryId = request.EntryId,
            EndpointId = "host",
            Kind = MessageKind.Response,
            Route = request.Route,
            Payload = payload,
            Error = error,
        };
    }

    private PluginManifestV3 ToV3Manifest()
    {
        return new PluginManifestV3
        {
            Id = _manifest.ParentId,
            Version = _manifest.Version,
            ProtocolVersion = "3.0",
            Entries = [new PluginEntryV3
            {
                EntryId = _manifest.EntryId,
                NodeEntry = _manifest.EntryFullPath,
                Capabilities = [],
            }],
        };
    }
}

/// <summary>Thrown when a plugin.call.* response carries an error.</summary>
internal sealed class BusCallException : System.Exception
{
    public BusCallException(string code, string message) : base($"{code}: {message}") { }
}
