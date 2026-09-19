using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Heartbeat;
using MyTools.Host.Core.Sessions;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Heartbeat;

[TestFixture]
public class SessionHeartbeatTest
{
    private static readonly SessionHeartbeatOptions FastOptions = new(
        TimeSpan.FromMilliseconds(1),
        TimeSpan.FromMilliseconds(10),
        3);

    [Test]
    public async Task RunAsync_ConsecutiveTimeouts_ShouldDeclarePeerDead()
    {
        var session = ReadySession();
        var client = new ScriptedRpcClient(
            TimeoutFailure,
            TimeoutFailure,
            TimeoutFailure);
        var heartbeat = CreateHeartbeat(session, client);

        var exit = await heartbeat.RunAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(exit.Kind, Is.EqualTo(HeartbeatExitKind.PeerDead));
            Assert.That(client.CallCount, Is.EqualTo(FastOptions.DeadAfter));
        });
    }

    [Test]
    public async Task RunAsync_SuccessfulPong_ShouldResetConsecutiveTimeouts()
    {
        var session = ReadySession();
        var client = new ScriptedRpcClient(
            TimeoutFailure,
            TimeoutFailure,
            Success,
            TimeoutFailure,
            TimeoutFailure,
            TimeoutFailure);
        var heartbeat = CreateHeartbeat(session, client);

        var exit = await heartbeat.RunAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(exit.Kind, Is.EqualTo(HeartbeatExitKind.PeerDead));
            Assert.That(client.CallCount, Is.EqualTo(6));
        });
    }

    [Test]
    public async Task RunAsync_WhenCancelled_ShouldStopSendingPings()
    {
        var session = ReadySession();
        var firstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ScriptedRpcClient((_, _) =>
        {
            firstCall.TrySetResult();
            return Task.FromResult<JsonNode?>(null);
        });
        var heartbeat = CreateHeartbeat(session, client);
        using var cancellation = new CancellationTokenSource();

        var runTask = heartbeat.RunAsync(cancellation.Token);
        await firstCall.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        var exit = await runTask.WaitAsync(TimeSpan.FromSeconds(1));
        var callsAtExit = client.CallCount;
        await Task.Delay(TimeSpan.FromMilliseconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(exit.Kind, Is.EqualTo(HeartbeatExitKind.Cancelled));
            Assert.That(client.CallCount, Is.EqualTo(callsAtExit));
        });
    }

    [Test]
    public async Task RunAsync_WhenTransportDisconnects_ShouldReturnTransportDisconnected()
    {
        var session = ReadySession();
        var client = new ScriptedRpcClient(TransportFailure);
        var heartbeat = CreateHeartbeat(session, client);

        var exit = await heartbeat.RunAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(exit.Kind, Is.EqualTo(HeartbeatExitKind.TransportDisconnected));
    }

    private static SessionHeartbeat CreateHeartbeat(
        PluginSession session,
        IEndpointRpcClient client)
        => new(
            session,
            session.GenerationGuard.Current,
            client,
            diagnostics: null,
            logger: null,
            FastOptions);

    private static PluginSession ReadySession()
    {
        var session = new PluginSession("settings", "session-1");
        session.Transition(SessionState.Starting);
        session.Transition(SessionState.Ready);
        return session;
    }

    private static Task<JsonNode?> Success(JsonNode? _, CancellationToken __)
        => Task.FromResult<JsonNode?>(null);

    private static Task<JsonNode?> TimeoutFailure(JsonNode? _, CancellationToken __)
        => Task.FromException<JsonNode?>(new RpcCallException(
            ErrorCode.RequestTimeout,
            "test ping timeout"));

    private static Task<JsonNode?> TransportFailure(JsonNode? _, CancellationToken __)
        => Task.FromException<JsonNode?>(new RpcCallException(
            ErrorCode.TransportDisconnected,
            "test transport disconnected"));

    private sealed class ScriptedRpcClient : IEndpointRpcClient
    {
        private readonly ConcurrentQueue<Func<JsonNode?, CancellationToken, Task<JsonNode?>>> _steps;
        private readonly Func<JsonNode?, CancellationToken, Task<JsonNode?>>? _fallback;
        private int _callCount;

        public ScriptedRpcClient(
            params Func<JsonNode?, CancellationToken, Task<JsonNode?>>[] steps)
        {
            _steps = new ConcurrentQueue<Func<JsonNode?, CancellationToken, Task<JsonNode?>>>(steps);
            _fallback = steps.LastOrDefault();
        }

        public EndpointId Endpoint { get; } = new(
            "settings",
            "session-1",
            EndpointIds.HostControl,
            IsNode: false);

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<JsonNode?> CallAsync(
            string route,
            JsonNode? payload,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            if (_steps.TryDequeue(out var step))
            {
                return step(payload, cancellationToken);
            }
            return _fallback!(payload, cancellationToken);
        }

        public void FailPending(BusError error) { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
