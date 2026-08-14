using System;
using System.Collections.Generic;

namespace MyTools.Host.Transports.Process;

/// <summary>
/// Restart backoff policy: exponential backoff with jitter, capped at <see cref="MaxDelay"/>, plus a
/// sliding time-window limit on restart count. Per the design: "带抖动的指数退避，并设置时间窗口
/// 内的最大重启次数". When the window's restart count is exhausted the plugin goes to Stopped and
/// must be restarted by the user or a host policy.
/// </summary>
public sealed class RestartPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly TimeSpan _window;
    private readonly int _maxRestartsPerWindow;
    private readonly double _jitter; // fraction of delay used as jitter band, e.g. 0.25 = ±25%
    private readonly Func<DateTime> _clock;
    private readonly Func<double, double> _random; // returns a value in [0, max)
    private readonly Queue<DateTime> _restartTimes = new();
    private int _consecutiveFailures;

    public RestartPolicy(
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        TimeSpan window,
        int maxRestartsPerWindow,
        double jitter = 0.2,
        Func<DateTime>? clock = null,
        Func<double, double>? random = null)
    {
        _baseDelay = baseDelay;
        _maxDelay = maxDelay;
        _window = window;
        _maxRestartsPerWindow = maxRestartsPerWindow;
        _jitter = jitter;
        _clock = clock ?? (() => DateTime.UtcNow);
        _random = random ?? (max => System.Random.Shared.NextDouble() * max);
    }

    /// <summary>Computes the next backoff delay for the current failure count (does not increment).</summary>
    public TimeSpan NextDelay()
    {
        var exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, _consecutiveFailures);
        var capped = Math.Min(exponential, _maxDelay.TotalMilliseconds);

        // Jitter: pick a factor in [1 - jitter, 1 + jitter] via _random(1) ∈ [0,1).
        var jitterFactor = 1.0 + (_random(1) - 0.5) * 2 * _jitter;
        var withJitter = capped * jitterFactor;

        return TimeSpan.FromMilliseconds(Math.Min(withJitter, _maxDelay.TotalMilliseconds));
    }

    /// <summary>Records that a restart attempt has occurred (advances failure count + window count).</summary>
    public void RecordRestart()
    {
        _consecutiveFailures++;
        _restartTimes.Enqueue(_clock());
        EvictOutsideWindow();
    }

    /// <summary>True while the number of restarts in the sliding window is below the limit.</summary>
    public bool CanRestart()
    {
        EvictOutsideWindow();
        return _restartTimes.Count < _maxRestartsPerWindow;
    }

    private void EvictOutsideWindow()
    {
        var now = _clock();
        var cutoff = now - _window;
        while (_restartTimes.Count > 0 && _restartTimes.Peek() < cutoff)
        {
            _restartTimes.Dequeue();
        }
    }
}
