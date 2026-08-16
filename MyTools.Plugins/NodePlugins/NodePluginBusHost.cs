using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Heartbeat;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Manifest;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// Message-bus runtime for a Node plugin entry. Each host method (<c>search</c>, <c>invokeAction</c>, …)
/// is mapped to a <c>plugin.call.&lt;method&gt;</c> envelope; responses are correlated by request id
/// via a registered host endpoint on the bus. Inbound <c>plugin.event.*</c> envelopes raise
/// <see cref="EventReceived"/>; <c>host.call.*</c> is handled by the <see cref="MessageBus"/> through
/// <see cref="HostCallHandler"/>.
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
    private EndpointId? _hostEndpoint;
    private HostEndpointTransport? _hostTransport;
    private CancellationTokenSource? _heartbeatCts;
    private int _started; // 0 = not started, 1 = started

    /// <summary>Host→Node ping interval.</summary>
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    /// <summary>Per-ping timeout before counting a consecutive miss.</summary>
    internal static readonly TimeSpan HeartbeatPingTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Consecutive missed pongs before declaring the peer dead.</summary>
    internal const int HeartbeatDeadAfter = 3;

    private const int DefaultTimeoutMs = 30000;

    public string NodeExePath { get; set; } = "node";

    public PluginSession? Session => _session;

    public string? SessionId => _session?.SessionId;

    public NodePluginBusHost(NodePluginManifest manifest, PluginSessionManager sessionManager,
        MessageBus bus, ILogger<NodePluginBusHost> logger, IIdGenerator? ids = null)
    {
        _manifest = manifest;
        _sessionManager = sessionManager;
        _bus = bus;
        _logger = logger;
        _ids = ids ?? new GuidIdGenerator();
        _sessionManager.SessionReplaced += OnSessionReplaced;
        _logger.LogInformation("NodePluginBusHost created for plugin={PluginId} entry={EntryId}",
            manifest.ParentId, manifest.EntryId);
    }

    public event EventHandler<NodePluginEventReceivedEventArgs>? EventReceived;

    public Func<HostCallRequest, CancellationToken, Task<JsonElement>>? HostCallHandler { get; set; }

    public async Task StartAsync(string nodeExePath, CancellationToken cancellationToken)
    {
        NodeExePath = nodeExePath;
        await EnsureStartedAsync(cancellationToken);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _started) == 1) return;

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (_started == 1) return;

            var v3 = ToV3Manifest();
            _session = await _sessionManager.StartSessionAsync(v3, _manifest.EntryId, NodeExePath, cancellationToken);
            _nodeEndpoint = new EndpointId(_manifest.ParentId, _manifest.EntryId, _session.SessionId,
                EndpointIds.NodeMain, IsNode: true);
            BindHostEndpoint();

            _bus.RegisterHostCallHandler(_manifest.ParentId, _manifest.EntryId, InvokeHostCallAsync);

            _heartbeatCts = new CancellationTokenSource();
            _ = RunHeartbeatAsync(_heartbeatCts.Token);

            Volatile.Write(ref _started, 1);
        }
        finally
        {
            _startLock.Release();
        }
    }

    private void OnSessionReplaced(object? sender, PluginSessionReplacedEventArgs e)
    {
        if (e.PluginId != _manifest.ParentId || e.EntryId != _manifest.EntryId) return;

        _logger.LogWarning("Session replaced for {PluginId}/{EntryId}: {Old} -> {New}",
            e.PluginId, e.EntryId, e.Previous.SessionId, e.Current.SessionId);

        FailLocalPending(ErrorCode.TransportDisconnected, "node session restarted");
        UnbindHostEndpoint();

        _session = e.Current;
        _nodeEndpoint = new EndpointId(_manifest.ParentId, _manifest.EntryId, _session.SessionId,
            EndpointIds.NodeMain, IsNode: true);
        BindHostEndpoint();
    }

    private void BindHostEndpoint()
    {
        if (_session is null) return;
        _hostTransport = new HostEndpointTransport();
        _hostTransport.Delivered += OnHostDelivery;
        _hostEndpoint = new EndpointId(_manifest.ParentId, _manifest.EntryId, _session.SessionId,
            EndpointIds.Host, IsNode: false);
        _bus.RegisterEndpoint(_hostEndpoint, _hostTransport);
    }

    private void UnbindHostEndpoint()
    {
        if (_hostEndpoint is not null)
        {
            _bus.UnregisterEndpoint(_hostEndpoint);
            _hostEndpoint = null;
        }

        if (_hostTransport is not null)
        {
            _hostTransport.Delivered -= OnHostDelivery;
            _ = _hostTransport.DisposeAsync();
            _hostTransport = null;
        }
    }

    private void FailLocalPending(ErrorCode code, string message)
    {
        foreach (var (_, tcs) in _pending)
        {
            tcs.TrySetException(new BusCallException(code.ToString(), message));
        }

        _pending.Clear();
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        var monitor = new HeartbeatMonitor(
            (long)HeartbeatPingTimeout.TotalMilliseconds,
            HeartbeatDeadAfter,
            () => Environment.TickCount64);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HeartbeatInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var session = _session;
            var hostEndpoint = _hostEndpoint;
            if (session is null || !session.IsAvailable || hostEndpoint is null) continue;

            var pingId = _ids.NewId();
            var waiter = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[pingId] = waiter;

            var ping = new Envelope
            {
                Version = ProtocolVersion.Current,
                Id = pingId,
                TraceId = pingId,
                SessionId = session.SessionId,
                PluginId = _manifest.ParentId,
                EntryId = _manifest.EntryId,
                EndpointId = EndpointIds.Host,
                Kind = MessageKind.Request,
                Route = Routes.Bus.Ping,
                TimeoutMs = (int)HeartbeatPingTimeout.TotalMilliseconds,
                Payload = JsonNode.Parse("""{"ok":true}"""),
            };

            monitor.OnPingSent();
            try
            {
                await _bus.RouteRequestAsync(ping, hostEndpoint);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "bus.ping send failed");
                _pending.TryRemove(pingId, out _);
                continue;
            }

            try
            {
                using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                pingCts.CancelAfter(HeartbeatPingTimeout);
                await waiter.Task.WaitAsync(pingCts.Token);
                monitor.OnPong();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _pending.TryRemove(pingId, out _);
                var check = monitor.CheckTimeout();
                if (check.NowDead)
                {
                    _logger.LogWarning(
                        "Node heartbeat dead for {PluginId}/{EntryId} after {N} timeouts; requesting restart",
                        _manifest.ParentId, _manifest.EntryId, HeartbeatDeadAfter);
                    await _sessionManager.NotifyPeerDeadAsync(_manifest.ParentId, _manifest.EntryId);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void OnHostDelivery(Envelope env)
    {
        if (!Routes.IsPing(env.Route))
        {
            _logger.LogInformation("HostDelivery: kind={Kind} route={Route} corr={CorrelationId}",
                env.Kind, env.Route, env.CorrelationId);
        }
        
        switch (env.Kind)
        {
            case MessageKind.Response:
                HandleResponse(env);
                break;
            case MessageKind.Event:
                HandleEvent(env);
                break;
        }
    }

    private async Task<JsonElement> InvokeHostCallAsync(
        string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (HostCallHandler is null)
        {
            throw new InvalidOperationException("No host call handler registered for this plugin.");
        }

        return await HostCallHandler(new HostCallRequest(
            method,
            parameters,
            _manifest.ParentId,
            _manifest.EntryId,
            _session?.SessionId ?? ""), cancellationToken);
    }

    public Task<JsonElement> InitializeAsync(string locale, string fallbackLocale,
        IReadOnlyDictionary<string, string> messages, string theme,
        CancellationToken cancellationToken = default)
    {
        return SendAndUnwrapResultAsync(Routes.PluginCall.Initialize, new NodePluginInitializeRequest
        {
            Locale = locale,
            FallbackLocale = fallbackLocale,
            Messages = messages,
            Theme = theme,
        }, cancellationToken);
    }

    public Task<NodePluginSearchResponse> SearchAsync(string query, string mode, string locale,
        string fallbackLocale, string theme, CancellationToken cancellationToken)
    {
        return SendAsync<NodePluginSearchResponse>(Routes.PluginCall.Search, new NodePluginSearchRequest
        {
            Query = query,
            Mode = mode,
            Locale = locale,
            FallbackLocale = fallbackLocale,
            Theme = theme,
        }, cancellationToken);
    }

    public Task<NodePluginActionResponse> InvokeActionAsync(string itemId, string actionId, string query,
        string locale, string fallbackLocale, string theme, CancellationToken cancellationToken = default)
    {
        return SendAsync<NodePluginActionResponse>(Routes.PluginCall.InvokeAction, new NodePluginActionRequest
        {
            ItemId = itemId,
            ActionId = actionId,
            Query = query,
            Locale = locale,
            FallbackLocale = fallbackLocale,
            Theme = theme,
        }, cancellationToken);
    }

    public void Dispose()
    {
        _sessionManager.SessionReplaced -= OnSessionReplaced;

        try { _heartbeatCts?.Cancel(); } catch { /* ignore */ }
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;

        FailLocalPending(ErrorCode.TransportDisconnected, "bus host disposed");
        UnbindHostEndpoint();
        _bus.UnregisterHostCallHandler(_manifest.ParentId, _manifest.EntryId);

        if (_session is not null)
        {
            _ = _sessionManager.StopSessionAsync(_manifest.ParentId, _manifest.EntryId, _session.SessionId);
        }
    }

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
        await EnsureStartedAsync(cancellationToken);

        if (_session is null || _hostEndpoint is null)
        {
            throw new InvalidOperationException("bus host failed to start");
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
            EndpointId = EndpointIds.Host,
            Kind = MessageKind.Request,
            Route = route,
            TimeoutMs = DefaultTimeoutMs,
            Payload = JsonNode.Parse(JsonSerializer.Serialize(parameters, ProtocolJsonOptions.Default)),
        };

        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeoutMs);
        var requestStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _logger.LogInformation("id: {Id}, start", id);
        using var _ = timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var timedOutTcs))
            {
                var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(requestStartedAt).TotalMilliseconds;
                _logger.LogWarning(
                    "plugin.call '{Route}' timed out after {ElapsedMs}ms (expected {TimeoutMs}ms)",
                    route, elapsedMs, DefaultTimeoutMs);
                timedOutTcs.TrySetException(new BusCallException(
                    ErrorCode.RequestTimeout.ToString(),
                    $"plugin.call '{route}' timed out after {DefaultTimeoutMs}ms"));
            }
        });

        await _bus.RouteRequestAsync(env, _hostEndpoint);
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

    private PluginManifestV3 ToV3Manifest()
    {
        return new PluginManifestV3
        {
            Id = _manifest.ParentId,
            Version = _manifest.Version,
            ProtocolVersion = ProtocolVersion.CurrentWire,
            Entries =
            [
                new PluginEntryV3
                {
                    Id = _manifest.EntryId,
                    Entry = _manifest.EntryFullPath,
                    Capabilities = _manifest.Capabilities,
                },
            ],
        };
    }
}

/// <summary>Thrown when a plugin.call.* response carries an error or times out.</summary>
internal sealed class BusCallException : Exception
{
    public BusCallException(string code, string message) : base($"{code}: {message}") { }
}
