using System;

namespace MyTools.Host.Core.Heartbeat;

/// <summary>Result of a watchdog timeout check.</summary>
public readonly record struct HeartbeatCheck(bool TimedOut, bool NowDead);

/// <summary>
/// Host-side heartbeat watchdog for the Host→Node <c>bus.ping</c>. The host periodically sends a
/// ping and records the pong; it uses a monotonic clock (milliseconds) to compute RTT. If
/// consecutive pings time out (peer did not pong within <c>timeoutMs</c>), the connection is
/// declared dead (假死) and the session is restarted by the caller. A single successful pong resets
/// the consecutive-timeout counter. Per the design this covers the case where the pipe hasn't broken
/// but the peer froze.
/// </summary>
public sealed class HeartbeatMonitor
{
    private readonly long _timeoutMs;
    private readonly int _deadAfter;
    private readonly Func<long> _clock; // monotonic milliseconds
    private long _lastPingMs = -1;
    private long _lastRttMs;

    public HeartbeatMonitor(long timeoutMs, int deadAfter, Func<long> clock)
    {
        _timeoutMs = timeoutMs;
        _deadAfter = deadAfter;
        _clock = clock;
    }

    /// <summary>Number of consecutive pings that have timed out without a pong.</summary>
    public int ConsecutiveTimeouts { get; private set; }

    /// <summary>True once <see cref="ConsecutiveTimeouts"/> reached <c>deadAfter</c>.</summary>
    public bool IsDead { get; private set; }

    /// <summary>RTT of the last successful pong, in milliseconds.</summary>
    public long LastRttMs => _lastRttMs;

    /// <summary>Call when a ping has just been sent.</summary>
    public void OnPingSent() => _lastPingMs = _clock();

    /// <summary>Call when a pong arrives. Always resets the timeout counter and dead state.</summary>
    public void OnPong()
    {
        if (_lastPingMs >= 0)
        {
            _lastRttMs = _clock() - _lastPingMs;
            _lastPingMs = -1;
        }
        // Any pong — even one arriving after a consumed ping — proves liveness: reset.
        ConsecutiveTimeouts = 0;
        IsDead = false;
    }

    /// <summary>
    /// Checks whether the current outstanding ping has exceeded the timeout. If so, increments the
    /// consecutive-timeout counter and declares dead when the threshold is reached. Call on a timer.
    /// </summary>
    public HeartbeatCheck CheckTimeout()
    {
        if (_lastPingMs < 0)
        {
            return new HeartbeatCheck(false, IsDead);
        }

        if (_clock() - _lastPingMs < _timeoutMs)
        {
            return new HeartbeatCheck(false, IsDead);
        }

        // Timed out.
        ConsecutiveTimeouts++;
        _lastPingMs = -1; // consume this ping
        if (ConsecutiveTimeouts >= _deadAfter)
        {
            IsDead = true;
        }
        return new HeartbeatCheck(true, IsDead);
    }
}
