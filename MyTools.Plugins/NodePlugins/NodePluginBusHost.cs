using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Heartbeat;
using MyTools.Host.Core.Sessions;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Manifest;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// Message-bus runtime for a Node plugin. Each host method (<c>search</c>, <c>invokeAction</c>, …)
/// is mapped to a <c>plugin.call.&lt;method&gt;</c> envelope; responses are correlated by request id
/// via a registered host endpoint on the bus. Inbound <c>plugin.event.*</c> envelopes raise
/// <see cref="EventReceived"/>; <c>host.call.*</c> is handled by <see cref="HostCallDispatcher"/>
/// through <see cref="HostCallHandler"/>.
/// </summary>
internal sealed class NodePluginBusHost : INodePluginHost
{
    private readonly NodePluginManifest _manifest;
    private readonly PluginSessionManager _sessionManager;
    private readonly MessageBus _bus;
    private readonly IPluginDiagnosticsService _diagnostics;
    private readonly ILogger<NodePluginBusHost> _logger;
    private readonly IIdGenerator _ids;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly object _disposeGate = new();

    private PluginSession? _session;
    private EndpointId? _nodeEndpoint;
    private EndpointRpcClient? _rpcClient;
    private EndpointRpcClient? _controlRpcClient;
    private CancellationTokenSource? _heartbeatCts;
    private int _started; // 0 = not started, 1 = started
    private int _disposed;
    private Task? _disposeTask;

    /// <summary>Host→Node ping interval.</summary>
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    /// <summary>Per-ping timeout before counting a consecutive miss.</summary>
    internal static readonly TimeSpan HeartbeatPingTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Consecutive missed pongs before declaring the peer dead.</summary>
    internal const int HeartbeatDeadAfter = 3;

    internal const int DefaultTimeoutMs = 30000;

    /// <summary>Host wait budget for <c>plugin.call.*</c>. Overridable in tests.</summary>
    internal int RequestTimeoutMs { get; set; } = DefaultTimeoutMs;

    public string NodeExePath { get; set; } = "node";

    public PluginSession? Session => _session;

    public string? SessionId => _session?.SessionId;

    public string? FailureDetails => _session?.Controller?.FailureDetails;

    public NodePluginBusHost(NodePluginManifest manifest, PluginSessionManager sessionManager,
        MessageBus bus, IPluginDiagnosticsService diagnostics, ILogger<NodePluginBusHost> logger, IIdGenerator? ids = null)
    {
        _manifest = manifest;
        _sessionManager = sessionManager;
        _bus = bus;
        _diagnostics = diagnostics;
        _logger = logger;
        _ids = ids ?? new GuidIdGenerator();
        _sessionManager.SessionReplaced += OnSessionReplaced;
        _sessionManager.SessionUnavailable += OnSessionUnavailable;
        _logger.LogInformation("NodePluginBusHost created for plugin={PluginId}", manifest.Id);
    }

    public event EventHandler<NodePluginEventReceivedEventArgs>? EventReceived;

    public Func<HostCallRequest, CancellationToken, Task<JsonElement>>? HostCallHandler { get; set; }

    public async Task StartAsync(string nodeExePath, CancellationToken cancellationToken)
    {
        NodeExePath = nodeExePath;
        await EnsureStartedAsync(cancellationToken);
    }

    public async Task StopSessionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        await _startLock.WaitAsync(cancellationToken);
        try
        {
            await StopSessionCoreAsync(recordDiagnostic: true);
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task RestartSessionAsync(CancellationToken cancellationToken = default)
    {
        await StopSessionAsync(cancellationToken);
        await StartAsync(NodeExePath, cancellationToken);
        _diagnostics.RecordDiagnostic(
            LogLevel.Information,
            "session.restart.requested",
            "User requested plugin restart.",
            _manifest.Id,
            _session?.SessionId);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        if (Volatile.Read(ref _started) == 1) return;

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
            if (_started == 1) return;

            var v3 = ToV3Manifest();
            _session = await _sessionManager.StartSessionAsync(v3, NodeExePath, cancellationToken);
            _nodeEndpoint = new EndpointId(_manifest.Id, _session.SessionId,
                EndpointIds.NodeMain, IsNode: true);
            BindHostEndpoint();

            _bus.HostCallDispatcher.RegisterHandler(
                _manifest.Id,
                InvokeHostCallAsync,
                _session.LifetimeToken);

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
        if (e.PluginId != _manifest.Id) return;

        _logger.LogWarning("Session replaced for {PluginId}: {Old} -> {New}",
            e.PluginId, e.Previous.SessionId, e.Current.SessionId);

        StopHeartbeat();
        FailRpcClients(ErrorCode.TransportDisconnected, "node session restarted");
        UnbindHostEndpoints();

        _session = e.Current;
        _nodeEndpoint = new EndpointId(_manifest.Id, _session.SessionId,
            EndpointIds.NodeMain, IsNode: true);
        BindHostEndpoint();
        _bus.HostCallDispatcher.RegisterHandler(
            _manifest.Id,
            InvokeHostCallAsync,
            _session.LifetimeToken);
        _heartbeatCts = new CancellationTokenSource();
        _ = RunHeartbeatAsync(_heartbeatCts.Token);
        Volatile.Write(ref _started, 1);
    }

    private void OnSessionUnavailable(object? sender, PluginSessionUnavailableEventArgs e)
    {
        if (e.PluginId != _manifest.Id)
        {
            return;
        }

        var current = _session;
        if (current is null || !string.Equals(current.SessionId, e.SessionId, StringComparison.Ordinal))
        {
            return;
        }

        _logger.LogWarning(
            "Session unavailable for plugin={PluginId} session={SessionId}; auto-restart budget exhausted",
            e.PluginId,
            e.SessionId);

        StopHeartbeat();
        FailRpcClients(ErrorCode.TransportDisconnected, "node session stopped");
        if (e.WillRestart)
        {
            return;
        }
        UnbindHostEndpoints();
        _session = null;
        _nodeEndpoint = null;
        Volatile.Write(ref _started, 0);
    }

    private void BindHostEndpoint()
    {
        if (_session is null) return;
        _rpcClient = new EndpointRpcClient(
            _bus,
            new EndpointId(_manifest.Id, _session.SessionId, EndpointIds.Host, IsNode: false),
            _ids,
            _diagnostics,
            _logger);
        _rpcClient.EventReceived += HandleEvent;
        _controlRpcClient = new EndpointRpcClient(
            _bus,
            new EndpointId(_manifest.Id, _session.SessionId, EndpointIds.HostControl, IsNode: false),
            _ids,
            _diagnostics,
            _logger);
    }

    private void UnbindHostEndpoints()
    {
        if (_rpcClient is not null)
        {
            _rpcClient.EventReceived -= HandleEvent;
            _ = _rpcClient.DisposeAsync();
            _rpcClient = null;
        }
        if (_controlRpcClient is not null)
        {
            _ = _controlRpcClient.DisposeAsync();
            _controlRpcClient = null;
        }
    }

    private void FailRpcClients(ErrorCode code, string message)
    {
        var error = BusError.For(code, message, retryable: true);
        _rpcClient?.FailPending(error);
        _controlRpcClient?.FailPending(error);
    }

    private void StopHeartbeat()
    {
        try { _heartbeatCts?.Cancel(); } catch { /* ignore */ }
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        var monitor = new HeartbeatMonitor(
            (long)HeartbeatPingTimeout.TotalMilliseconds,
            HeartbeatDeadAfter,
            () => Environment.TickCount64);

        try
        {
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
                var controlClient = _controlRpcClient;
                if (session is null || !session.IsAvailable || controlClient is null) continue;

                monitor.OnPingSent();
                try
                {
                    await controlClient.CallAsync(
                        Routes.Bus.Ping,
                        JsonNode.Parse("""{"ok":true}"""),
                        HeartbeatPingTimeout,
                        cancellationToken);
                    monitor.OnPong();
                }
                catch (RpcCallException ex) when (ex.Code == ErrorCode.TransportDisconnected)
                {
                    _logger.LogDebug(ex,
                        "Node heartbeat stopped for {PluginId} because the transport disconnected",
                        _manifest.Id);
                    break;
                }
                catch (RpcCallException ex) when (ex.Code == ErrorCode.RequestTimeout)
                {
                    var check = monitor.CheckTimeout();
                    _diagnostics.RecordHeartbeatTimeout(_manifest.Id, session.SessionId, monitor.ConsecutiveTimeouts, check.NowDead);
                    if (check.NowDead)
                    {
                        _logger.LogWarning(
                            "Node heartbeat dead for {PluginId} after {N} timeouts; requesting restart",
                            _manifest.Id, HeartbeatDeadAfter);
                        await _sessionManager.NotifyPeerDeadAsync(_manifest.Id);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (RpcCallException ex) when (ex.Code == ErrorCode.Cancelled && cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (RpcCallException ex) when (ex.Code == ErrorCode.TransportDisconnected)
        {
            _logger.LogDebug(ex,
                "Node heartbeat stopped for {PluginId} because the transport disconnected",
                _manifest.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Node heartbeat loop failed for {PluginId}", _manifest.Id);
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
            _manifest.Id,
            _session?.SessionId ?? ""), cancellationToken);
    }

    public Task<NodePluginInitializeResponse> InitializeAsync(string locale, string fallbackLocale,
        IReadOnlyDictionary<string, string> messages, string theme,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NodePluginInitializeResponse>(Routes.PluginCall.Initialize, new NodePluginInitializeRequest
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

    public Task<NodePluginActionOutcome> InvokeActionAsync(string itemId, string actionId, string query,
        string locale, string fallbackLocale, string theme, CancellationToken cancellationToken = default)
    {
        return SendAsync<NodePluginActionOutcome>(Routes.PluginCall.InvokeAction, new NodePluginActionRequest
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
        _ = DisposeAsync();
    }

    public Task DisposeAsync()
    {
        lock (_disposeGate)
        {
            return _disposeTask ??= DisposeCoreAsync();
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await _startLock.WaitAsync();
        try
        {
            _sessionManager.SessionReplaced -= OnSessionReplaced;
            _sessionManager.SessionUnavailable -= OnSessionUnavailable;

            StopHeartbeat();
            FailRpcClients(ErrorCode.TransportDisconnected, "bus host disposed");
            UnbindHostEndpoints();
            _bus.HostCallDispatcher.UnregisterHandler(_manifest.Id);

            if (_session is not null)
            {
                await StopSessionCoreAsync(recordDiagnostic: false);
            }
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task StopSessionCoreAsync(bool recordDiagnostic)
    {
        if (_session is null)
        {
            Volatile.Write(ref _started, 0);
            return;
        }

        if (recordDiagnostic)
        {
            _diagnostics.RecordDiagnostic(
                LogLevel.Information,
                "session.stop.requested",
                "User requested plugin stop.",
                _manifest.Id,
                _session.SessionId);
        }

        StopHeartbeat();
        FailRpcClients(ErrorCode.TransportDisconnected, "node session stopped");
        UnbindHostEndpoints();
        _bus.HostCallDispatcher.UnregisterHandler(_manifest.Id);

        var sessionId = _session.SessionId;
        await _sessionManager.StopSessionAsync(_manifest.Id, sessionId);
        _session = null;
        _nodeEndpoint = null;
        Volatile.Write(ref _started, 0);
    }

    private async Task<T> SendAsync<T>(string route, object parameters, CancellationToken cancellationToken)
    {
        var payloadJson = await SendAndAwaitResponseAsync(route, parameters, cancellationToken);
        var result = payloadJson is null
            ? default
            : JsonSerializer.Deserialize<T>(payloadJson.ToJsonString(), ProtocolJsonOptions.Default);
        return result ?? (typeof(T) == typeof(string) ? (T)(object)string.Empty : default!);
    }

    private async Task<JsonNode?> SendAndAwaitResponseAsync(string route, object parameters,
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);

        if (_session is null || _rpcClient is null)
        {
            throw new InvalidOperationException("bus host failed to start");
        }
        try
        {
            return await _rpcClient.CallAsync(
                route,
                JsonNode.Parse(JsonSerializer.Serialize(parameters, ProtocolJsonOptions.Default)),
                TimeSpan.FromMilliseconds(RequestTimeoutMs),
                cancellationToken);
        }
        catch (RpcCallException exception)
        {
            throw new BusCallException(exception.Code, exception.DetailMessage);
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
            Id = _manifest.Id,
            Version = _manifest.Version,
            ProtocolVersion = ProtocolVersion.CurrentWire,
            Entry = _manifest.EntryFullPath,
            Capabilities = _manifest.Capabilities,
        };
    }
}

/// <summary>Thrown when a plugin.call.* response carries an error, is cancelled, or times out.</summary>
internal sealed class BusCallException : RpcCallException
{
    public BusCallException(ErrorCode code, string message) : base(code, message) { }
}
