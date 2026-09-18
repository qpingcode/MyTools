using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Sessions;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Routing;

namespace MyTools.Host.Core.Heartbeat;

public sealed record SessionHeartbeatOptions(
    TimeSpan Interval,
    TimeSpan PingTimeout,
    int DeadAfter)
{
    public static SessionHeartbeatOptions Default { get; } = new(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        3);

    internal void Validate()
    {
        if (Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Interval));
        }
        if (PingTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PingTimeout));
        }
        if (DeadAfter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DeadAfter));
        }
    }
}

internal enum HeartbeatExitKind
{
    Cancelled,
    PeerDead,
    TransportDisconnected,
    HostFault,
}

internal sealed record HeartbeatExit(HeartbeatExitKind Kind, Exception? Exception = null)
{
    public static HeartbeatExit Cancelled() => new(HeartbeatExitKind.Cancelled);
    public static HeartbeatExit PeerDead() => new(HeartbeatExitKind.PeerDead);
    public static HeartbeatExit TransportDisconnected() => new(HeartbeatExitKind.TransportDisconnected);
    public static HeartbeatExit HostFault(Exception exception) =>
        new(HeartbeatExitKind.HostFault, exception);
}

/// <summary>Probes one immutable plugin session through its dedicated control RPC endpoint.</summary>
internal sealed class SessionHeartbeat
{
    private const string PingPayloadJson = "{\"ok\":true}";

    private readonly PluginSession _session;
    private readonly GenerationToken _generation;
    private readonly IEndpointRpcClient _controlRpcClient;
    private readonly IPluginDiagnosticsService? _diagnostics;
    private readonly ILogger _logger;
    private readonly SessionHeartbeatOptions _options;

    public SessionHeartbeat(
        PluginSession session,
        GenerationToken generation,
        IEndpointRpcClient controlRpcClient,
        IPluginDiagnosticsService? diagnostics,
        ILogger? logger,
        SessionHeartbeatOptions options)
    {
        options.Validate();
        _session = session;
        _generation = generation;
        _controlRpcClient = controlRpcClient;
        _diagnostics = diagnostics;
        _logger = logger ?? NullLogger.Instance;
        _options = options;
    }

    public async Task<HeartbeatExit> RunAsync(CancellationToken cancellationToken)
    {
        var monitor = new HeartbeatMonitor(
            (long)Math.Ceiling(_options.PingTimeout.TotalMilliseconds),
            _options.DeadAfter,
            () => Environment.TickCount64);

        while (true)
        {
            try
            {
                await Task.Delay(_options.Interval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return HeartbeatExit.Cancelled();
            }

            if (!_session.GenerationGuard.IsCurrent(_generation)
                || _session.State != SessionState.Ready)
            {
                return HeartbeatExit.Cancelled();
            }

            monitor.OnPingSent();
            try
            {
                await _controlRpcClient.CallAsync(
                    Routes.Bus.Ping,
                    JsonNode.Parse(PingPayloadJson),
                    _options.PingTimeout,
                    cancellationToken);
                monitor.OnPong();
                _logger.LogTrace(
                    "Node heartbeat pong plugin={PluginId} session={SessionId} rttMs={RttMs}",
                    _session.PluginId,
                    _session.SessionId,
                    monitor.LastRttMs);
            }
            catch (RpcCallException exception) when (exception.Code == ErrorCode.RequestTimeout)
            {
                var check = monitor.OnTimeout();
                _diagnostics?.RecordHeartbeatTimeout(
                    _session.PluginId,
                    _session.SessionId,
                    monitor.ConsecutiveTimeouts,
                    check.NowDead);
                if (check.NowDead)
                {
                    return HeartbeatExit.PeerDead();
                }
            }
            catch (RpcCallException exception) when (exception.Code == ErrorCode.TransportDisconnected)
            {
                return HeartbeatExit.TransportDisconnected();
            }
            catch (RpcCallException exception) when (
                exception.Code == ErrorCode.Cancelled && cancellationToken.IsCancellationRequested)
            {
                return HeartbeatExit.Cancelled();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return HeartbeatExit.Cancelled();
            }
            catch (RpcCallException exception)
            {
                return HeartbeatExit.HostFault(exception);
            }
        }
    }
}

internal sealed class SessionHeartbeatLease
{
    public required IEndpointRpcClient ControlRpcClient { get; init; }
    public required SessionHeartbeat Worker { get; init; }
    public required CancellationTokenSource Cancellation { get; init; }
    public required Task<HeartbeatExit> RunTask { get; init; }
    public Task ObserverTask { get; set; } = Task.CompletedTask;
}
