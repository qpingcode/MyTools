using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using MyTools.Common.Localization;
using MyTools.Host.Core.Diagnostics;

namespace MyTools.Desktop.Services;

internal sealed record PluginDiagnosticsAlertCandidate(
    string Key,
    LogLevel Level,
    string Title,
    string Message);

internal sealed class PluginDiagnosticsAlertEvaluator
{
    public static readonly TimeSpan QueuePressureThreshold = TimeSpan.FromSeconds(10);
    public const int RepeatedDisconnectThreshold = 2;
    public const int RepeatedRestartThreshold = 2;
    public const int TimeoutThreshold = 3;
    public const int ErrorThreshold = 5;
    public const int TooManyRequestsThreshold = 3;
    public const int SlowCallThreshold = 3;
    public const double SlowCallP95ThresholdMs = 5_000;

    public IReadOnlyList<PluginDiagnosticsAlertCandidate> Evaluate(PluginDiagnosticsSnapshot snapshot)
    {
        var alerts = new List<PluginDiagnosticsAlertCandidate>();
        foreach (var plugin in snapshot.Plugins)
        {
            if (plugin.HeartbeatDead.Recent > 0)
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:heartbeat-dead",
                    LogLevel.Error,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' heartbeat is dead."));
            }

            if (plugin.Disconnects.Recent >= RepeatedDisconnectThreshold)
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:disconnects",
                    LogLevel.Warning,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' disconnected repeatedly."));
            }

            if (plugin.Restarts.Recent >= RepeatedRestartThreshold)
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:restarts",
                    LogLevel.Warning,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' restarted repeatedly."));
            }

            if (plugin.RequestTimeouts.Recent >= TimeoutThreshold)
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:timeouts",
                    LogLevel.Warning,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' is timing out repeatedly."));
            }

            if (plugin.Errors.Recent >= ErrorThreshold)
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:errors",
                    LogLevel.Warning,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' is failing repeatedly."));
            }

            if (plugin.TooManyRequests.Recent >= TooManyRequestsThreshold)
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:too-many-requests",
                    LogLevel.Warning,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' is hitting the request cap."));
            }

            if (plugin.EventQueueDrops.Recent > 0)
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:event-drops",
                    LogLevel.Warning,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' dropped queued events."));
            }

            if (plugin.ProcessExits.Recent > 0 && plugin.LastExitCode is not null and not 0)
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:process-exit",
                    LogLevel.Error,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' exited with code {plugin.LastExitCode}."));
            }

            if (plugin.RestartExhaustions.Recent > 0)
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:restart-exhausted",
                    LogLevel.Error,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' exhausted automatic restarts."));
            }

            if (plugin.CallMetrics.Any(metric =>
                    metric.RecentSlowCount >= SlowCallThreshold
                    || (metric.Latency.SampleCount >= 5 && metric.Latency.P95Ms >= SlowCallP95ThresholdMs)))
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:slow-calls",
                    LogLevel.Warning,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' has sustained slow calls."));
            }

            if (plugin.Endpoints.Any(endpoint =>
                    endpoint.EventQueueHighPressureDurationMs >= QueuePressureThreshold.TotalMilliseconds
                    || endpoint.EventQueueUsageRatio >= 1.0))
            {
                alerts.Add(new PluginDiagnosticsAlertCandidate(
                    $"{plugin.PluginId}:queue-pressure",
                    LogLevel.Warning,
                    "Diagnostics",
                    $"Plugin '{plugin.PluginId}' event queue is under sustained pressure."));
            }
        }

        return alerts;
    }
}

internal sealed class PluginDiagnosticsAlertThrottler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _lastPublished = new(StringComparer.Ordinal);

    public PluginDiagnosticsAlertThrottler(TimeSpan minimumInterval)
    {
        MinimumInterval = minimumInterval;
    }

    public TimeSpan MinimumInterval { get; }

    public bool ShouldPublish(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_lastPublished.TryGetValue(key, out var previous)
                && now - previous < MinimumInterval)
            {
                return false;
            }

            _lastPublished[key] = now;
            return true;
        }
    }
}

public interface ITrayNotificationService
{
    void Attach(TaskbarIcon notifyIcon);
    void Detach(TaskbarIcon notifyIcon);
    void Show(string title, string message, LogLevel level);
}

public sealed class TrayNotificationService : ITrayNotificationService
{
    private readonly object _gate = new();
    private TaskbarIcon? _notifyIcon;

    public void Attach(TaskbarIcon notifyIcon)
    {
        lock (_gate)
        {
            _notifyIcon = notifyIcon;
        }
    }

    public void Detach(TaskbarIcon notifyIcon)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_notifyIcon, notifyIcon))
            {
                _notifyIcon = null;
            }
        }
    }

    public void Show(string title, string message, LogLevel level)
    {
        TaskbarIcon? notifyIcon;
        lock (_gate)
        {
            notifyIcon = _notifyIcon;
        }

        if (notifyIcon is null)
        {
            return;
        }

        void ShowCore()
        {
            notifyIcon.ShowBalloonTip(title, message, level switch
            {
                LogLevel.Error or LogLevel.Critical => BalloonIcon.Error,
                LogLevel.Warning => BalloonIcon.Warning,
                _ => BalloonIcon.Info
            });
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ShowCore();
            return;
        }

        dispatcher.Invoke(ShowCore);
    }
}

public sealed class PluginDiagnosticsAlertService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PublishInterval = TimeSpan.FromMinutes(2);

    private readonly IPluginDiagnosticsService _diagnostics;
    private readonly ITrayNotificationService _trayNotificationService;
    private readonly PluginDiagnosticsAlertEvaluator _evaluator = new();
    private readonly PluginDiagnosticsAlertThrottler _throttler = new(PublishInterval);
    private readonly ILogger<PluginDiagnosticsAlertService> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public PluginDiagnosticsAlertService(
        IPluginDiagnosticsService diagnostics,
        ITrayNotificationService trayNotificationService,
        ILocalizationService localizationService,
        ILogger<PluginDiagnosticsAlertService> logger)
    {
        _diagnostics = diagnostics;
        _trayNotificationService = trayNotificationService;
        _logger = logger;
        Title = localizationService.GetCaption("PluginDiagnostics.Menu", "Diagnostics");
        _loop = Task.Run(RunAsync);
    }

    public string Title { get; }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore best-effort shutdown
        }
        _cts.Dispose();
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                var snapshot = _diagnostics.GetSnapshot();
                foreach (var alert in _evaluator.Evaluate(snapshot))
                {
                    if (!_throttler.ShouldPublish(alert.Key, snapshot.CapturedAt))
                    {
                        continue;
                    }

                    _logger.Log(alert.Level,
                        "Publishing plugin diagnostics alert key={key} title={title} message={message}",
                        alert.Key, alert.Title, alert.Message);
                    _trayNotificationService.Show(Title, alert.Message, alert.Level);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}
