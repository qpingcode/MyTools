using System.Collections.Generic;

namespace MyTools.Host.Core.Backpressure;

/// <summary>
/// Per-endpoint, per-direction admission counter for in-flight requests. Enforces the design's
/// "pending request 上限" rule (default 64): once the cap is reached, further requests are rejected
/// (caller returns <c>TooManyRequests</c>). <c>bus.ping</c> and its response are exempt so request
/// congestion cannot cause false dead-peer detection.
///
/// The caller reserves/releases by an opaque request key (typically the request id); the route is
/// consulted only for the ping exemption.
/// </summary>
public sealed class PendingRequestTracker
{
    private readonly HashSet<string> _inFlight = new();

    public PendingRequestTracker(int limit = 64) => Limit = limit;

    public int Limit { get; }
    public int InFlight => _inFlight.Count;

    /// <summary>
    /// Attempts to admit a request identified by <paramref name="requestKey"/>. Returns false
    /// (caller rejects with TooManyRequests) if the cap is reached. <c>bus.ping</c> is always
    /// admitted and does not consume a slot.
    /// </summary>
    public bool TryReserve(string requestKey, string route = "")
    {
        if (route == "bus.ping") return true;
        if (_inFlight.Count >= Limit) return false;
        return _inFlight.Add(requestKey);
    }

    /// <summary>Releases a slot on response/timeout/disconnect. Ping release is a no-op.</summary>
    public void Release(string requestKey, string route = "")
    {
        if (route == "bus.ping") return;
        _inFlight.Remove(requestKey);
    }
}
