using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyTools.Common.Localization;
using MyTools.Desktop.Services;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Sessions;

namespace MyTools.Desktop.ViewModels;

public sealed partial class PluginDiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly PluginDiagnosticsCoordinator _coordinator;
    private readonly IPluginDiagnosticsService _diagnostics;
    private readonly ILocalizationService _localization;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, PluginDiagnosticsPluginItem> _sourceItems = new(StringComparer.Ordinal);
    private PluginDiagnosticsSnapshot _snapshot = new(DateTimeOffset.UtcNow, 0, [], []);

    public PluginDiagnosticsViewModel(
        PluginDiagnosticsCoordinator coordinator,
        IPluginDiagnosticsService diagnostics,
        ILocalizationService localization)
    {
        _coordinator = coordinator;
        _diagnostics = diagnostics;
        _localization = localization;
        WindowTitle = L("PluginDiagnostics.Menu", "Diagnostics");
        LevelFilters =
        [
            new FilterOption<LogLevel?>(
                L("PluginDiagnostics.Filter.AllLevels", "All levels"),
                null),
            new FilterOption<LogLevel?>("Information+", LogLevel.Information),
            new FilterOption<LogLevel?>("Warning+", LogLevel.Warning),
            new FilterOption<LogLevel?>("Error+", LogLevel.Error)
        ];
        TimeWindowFilters =
        [
            new FilterOption<TimeSpan?>(
                L("PluginDiagnostics.Filter.AllTime", "All time"),
                null),
            new FilterOption<TimeSpan?>(
                L("PluginDiagnostics.Filter.Last15Minutes", "Last 15 minutes"),
                TimeSpan.FromMinutes(15)),
            new FilterOption<TimeSpan?>(
                L("PluginDiagnostics.Filter.LastHour", "Last hour"),
                TimeSpan.FromHours(1)),
            new FilterOption<TimeSpan?>(
                L("PluginDiagnostics.Filter.Last6Hours", "Last 6 hours"),
                TimeSpan.FromHours(6))
        ];
        SelectedLevelFilter = LevelFilters[0];
        SelectedTimeWindowFilter = TimeWindowFilters[1];
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += (_, _) => Refresh();
        Refresh();
        _timer.Start();
    }

    public string WindowTitle { get; }
    public IReadOnlyList<FilterOption<LogLevel?>> LevelFilters { get; }
    public IReadOnlyList<FilterOption<TimeSpan?>> TimeWindowFilters { get; }
    public ObservableCollection<PluginDiagnosticsPluginListItem> Plugins { get; } = [];
    public ObservableCollection<PluginEndpointRow> EndpointMetrics { get; } = [];
    public ObservableCollection<PluginCallMetricRow> CallMetrics { get; } = [];
    public ObservableCollection<PluginEventMetricRow> EventMetrics { get; } = [];
    public ObservableCollection<PluginRecordRow> Records { get; } = [];

    [ObservableProperty]
    private PluginDiagnosticsPluginListItem? selectedPlugin;

    [ObservableProperty]
    private FilterOption<LogLevel?> selectedLevelFilter;

    [ObservableProperty]
    private FilterOption<TimeSpan?> selectedTimeWindowFilter;

    [ObservableProperty]
    private string sessionFilter = string.Empty;

    [ObservableProperty]
    private string selectedPluginName = string.Empty;

    [ObservableProperty]
    private string selectedPluginId = string.Empty;

    [ObservableProperty]
    private string selectedVersion = "-";

    [ObservableProperty]
    private string selectedRuntime = "-";

    [ObservableProperty]
    private string selectedState = "-";

    [ObservableProperty]
    private string selectedSessionId = "-";

    [ObservableProperty]
    private string selectedPid = "-";

    [ObservableProperty]
    private string selectedEnabled = "-";

    [ObservableProperty]
    private string failureDetails = string.Empty;

    [ObservableProperty]
    private string workingSet = "-";

    [ObservableProperty]
    private string privateMemory = "-";

    [ObservableProperty]
    private string cpuUsage = "-";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartCommand))]
    private bool canStop;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestartCommand))]
    private bool canRestart;

    [ObservableProperty] private long disconnectCount;
    [ObservableProperty] private long restartCount;
    [ObservableProperty] private long heartbeatTimeoutCount;
    [ObservableProperty] private long heartbeatDeadCount;
    [ObservableProperty] private long requestTimeoutCount;
    [ObservableProperty] private long errorCount;
    [ObservableProperty] private long tooManyRequestsCount;
    [ObservableProperty] private long eventDropsCount;
    [ObservableProperty] private long processExitCount;
    [ObservableProperty] private long restartExhaustionCount;

    private void Refresh()
    {
        var selectedKey = SelectedPlugin?.IdentityKey;
        var (plugins, snapshot) = _coordinator.GetSnapshot();
        _snapshot = snapshot;
        _sourceItems.Clear();
        Plugins.Clear();

        foreach (var item in plugins)
        {
            _sourceItems[item.IdentityKey] = item;
            Plugins.Add(new PluginDiagnosticsPluginListItem
            {
                IdentityKey = item.IdentityKey,
                PluginId = item.PluginId,
                DisplayName = item.DisplayName,
                Version = item.Version ?? "-",
                RuntimeKind = item.RuntimeKind,
                IsEnabled = item.IsEnabled,
                SessionId = item.RuntimeSnapshot?.CurrentSessionId ?? item.NodePlugin?.BusSessionId ?? "-",
                Pid = item.RuntimeSnapshot?.Pid?.ToString() ?? "-",
                State = FormatState(item)
            });
        }

        SelectedPlugin = Plugins.FirstOrDefault(item => item.IdentityKey == selectedKey)
                         ?? Plugins.FirstOrDefault();
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        EndpointMetrics.Clear();
        CallMetrics.Clear();
        EventMetrics.Clear();
        Records.Clear();

        if (SelectedPlugin is null || !_sourceItems.TryGetValue(SelectedPlugin.IdentityKey, out var item))
        {
            ResetDetails();
            return;
        }

        var runtime = item.RuntimeSnapshot;
        SelectedPluginName = item.DisplayName;
        SelectedPluginId = item.PluginId;
        SelectedVersion = item.Version ?? "-";
        SelectedRuntime = item.RuntimeKind;
        SelectedState = FormatState(item);
        SelectedSessionId = runtime?.CurrentSessionId ?? item.NodePlugin?.BusSessionId ?? "-";
        SelectedPid = runtime?.Pid?.ToString() ?? "-";
        SelectedEnabled = item.IsEnabled
            ? L("PluginDiagnostics.Enabled.Yes", "Enabled")
            : L("PluginDiagnostics.Enabled.No", "Disabled");
        FailureDetails = runtime?.FailureDetails
                         ?? item.NodePlugin?.BackendFailureDetails
                         ?? string.Empty;
        WorkingSet = FormatBytes(runtime?.Process?.WorkingSetBytes);
        PrivateMemory = FormatBytes(runtime?.Process?.PrivateMemoryBytes);
        CpuUsage = runtime?.Process is null ? "-" : $"{runtime.Process.CpuPercent:0.0}%";
        DisconnectCount = runtime?.Disconnects.Total ?? 0;
        RestartCount = runtime?.Restarts.Total ?? 0;
        HeartbeatTimeoutCount = runtime?.HeartbeatTimeouts.Total ?? 0;
        HeartbeatDeadCount = runtime?.HeartbeatDead.Total ?? 0;
        RequestTimeoutCount = runtime?.RequestTimeouts.Total ?? 0;
        ErrorCount = runtime?.Errors.Total ?? 0;
        TooManyRequestsCount = runtime?.TooManyRequests.Total ?? 0;
        EventDropsCount = runtime?.EventQueueDrops.Total ?? 0;
        ProcessExitCount = runtime?.ProcessExits.Total ?? 0;
        RestartExhaustionCount = runtime?.RestartExhaustions.Total ?? 0;
        CanStop = _coordinator.CanStop(item);
        CanRestart = _coordinator.CanRestart(item);

        if (runtime is not null)
        {
            foreach (var endpoint in runtime.Endpoints.OrderBy(entry => entry.EndpointId, StringComparer.Ordinal))
            {
                EndpointMetrics.Add(new PluginEndpointRow
                {
                    EndpointId = endpoint.EndpointId,
                    SessionId = endpoint.SessionId,
                    Pending = $"{endpoint.PendingInFlight}/{endpoint.PendingLimit}",
                    PendingHighWater = endpoint.PendingHighWater,
                    Queue = $"{endpoint.EventQueueDepth}/{endpoint.EventQueueCapacity}",
                    QueueHighWater = endpoint.EventQueueHighWater,
                    QueueUsage = $"{endpoint.EventQueueUsageRatio:P0}",
                    OldestWait = FormatDuration(endpoint.EventQueueOldestWaitMs),
                    QueueDrops = $"{endpoint.EventQueueDroppedTotal} ({endpoint.EventQueueDroppedRecent} recent)",
                    TooManyRequests = $"{endpoint.TooManyRequestsTotal} ({endpoint.TooManyRequestsRecent} recent)",
                    HighPressure = FormatDuration(endpoint.EventQueueHighPressureDurationMs),
                    MaxHighPressure = FormatDuration(endpoint.EventQueueMaxHighPressureDurationMs)
                });
            }

            foreach (var call in runtime.CallMetrics.OrderBy(entry => entry.EndpointId).ThenBy(entry => entry.Route))
            {
                CallMetrics.Add(new PluginCallMetricRow
                {
                    EndpointId = call.EndpointId,
                    Route = call.Route,
                    Counts = $"{call.CallCount}/{call.SuccessCount}/{call.FailureCount}/{call.TimeoutCount}/{call.RejectedCount}",
                    Last = FormatDuration(call.Latency.RecentMs),
                    Average = FormatDuration(call.Latency.AverageMs),
                    Max = FormatDuration(call.Latency.MaxMs),
                    P50 = FormatDuration(call.Latency.P50Ms),
                    P95 = FormatDuration(call.Latency.P95Ms),
                    P99 = FormatDuration(call.Latency.P99Ms),
                    RecentSlowCount = call.RecentSlowCount
                });
            }

            foreach (var metric in runtime.EventMetrics.OrderBy(entry => entry.EndpointId).ThenBy(entry => entry.Route))
            {
                EventMetrics.Add(new PluginEventMetricRow
                {
                    EndpointId = metric.EndpointId,
                    Route = metric.Route,
                    EventCount = metric.EventCount,
                    QueueWaitP50 = FormatDuration(metric.QueueWait.P50Ms),
                    QueueWaitP95 = FormatDuration(metric.QueueWait.P95Ms),
                    QueueWaitP99 = FormatDuration(metric.QueueWait.P99Ms),
                    DeliveryP50 = FormatDuration(metric.Delivery.P50Ms),
                    DeliveryP95 = FormatDuration(metric.Delivery.P95Ms),
                    DeliveryP99 = FormatDuration(metric.Delivery.P99Ms)
                });
            }
        }

        foreach (var record in FilterRecords(item))
        {
            Records.Add(new PluginRecordRow
            {
                Timestamp = $"{record.Timestamp:HH:mm:ss.fff} #{record.Sequence}",
                Level = record.Level.ToString(),
                SessionId = record.SessionId ?? string.Empty,
                EndpointId = record.EndpointId ?? string.Empty,
                Route = record.Route ?? string.Empty,
                CorrelationId = record.CorrelationId ?? string.Empty,
                Message = record.Message,
                Details = record.Details ?? string.Empty
            });
        }
    }

    [RelayCommand]
    private void RefreshWindow() => Refresh();

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (SelectedPlugin is null || !_sourceItems.TryGetValue(SelectedPlugin.IdentityKey, out var item))
        {
            return;
        }

        try
        {
            await _coordinator.StopAsync(item);
        }
        catch (Exception ex)
        {
            _diagnostics.RecordDiagnostic(
                LogLevel.Error,
                "ui.stop.failed",
                $"Failed to stop plugin '{item.PluginId}'.",
                item.PluginId,
                item.RuntimeSnapshot?.CurrentSessionId,
                details: ex.ToString());
        }

        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartAsync()
    {
        if (SelectedPlugin is null || !_sourceItems.TryGetValue(SelectedPlugin.IdentityKey, out var item))
        {
            return;
        }

        try
        {
            await _coordinator.RestartAsync(item);
        }
        catch (Exception ex)
        {
            _diagnostics.RecordDiagnostic(
                LogLevel.Error,
                "ui.restart.failed",
                $"Failed to restart plugin '{item.PluginId}'.",
                item.PluginId,
                item.RuntimeSnapshot?.CurrentSessionId,
                details: ex.ToString());
        }

        Refresh();
    }

    partial void OnSelectedPluginChanged(PluginDiagnosticsPluginListItem? value) => RefreshDetails();
    partial void OnSelectedLevelFilterChanged(FilterOption<LogLevel?> value) => RefreshDetails();
    partial void OnSelectedTimeWindowFilterChanged(FilterOption<TimeSpan?> value) => RefreshDetails();
    partial void OnSessionFilterChanged(string value) => RefreshDetails();

    public void Dispose()
    {
        _timer.Stop();
    }

    private IEnumerable<PluginDiagnosticRecord> FilterRecords(PluginDiagnosticsPluginItem item)
    {
        IEnumerable<PluginDiagnosticRecord> query = _snapshot.Records
            .Where(record => string.Equals(record.PluginId, item.PluginId, StringComparison.OrdinalIgnoreCase));

        if (SelectedLevelFilter.Value is { } minimumLevel)
        {
            query = query.Where(record => record.Level >= minimumLevel);
        }

        if (!string.IsNullOrWhiteSpace(SessionFilter))
        {
            query = query.Where(record =>
                record.SessionId?.Contains(SessionFilter, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (SelectedTimeWindowFilter.Value is { } window)
        {
            var threshold = _snapshot.CapturedAt - window;
            query = query.Where(record => record.Timestamp >= threshold);
        }

        return query
            .OrderBy(record => record.Timestamp)
            .ThenBy(record => record.Sequence);
    }

    private void ResetDetails()
    {
        SelectedPluginName = string.Empty;
        SelectedPluginId = string.Empty;
        SelectedVersion = "-";
        SelectedRuntime = "-";
        SelectedState = "-";
        SelectedSessionId = "-";
        SelectedPid = "-";
        SelectedEnabled = "-";
        FailureDetails = string.Empty;
        WorkingSet = "-";
        PrivateMemory = "-";
        CpuUsage = "-";
        DisconnectCount = 0;
        RestartCount = 0;
        HeartbeatTimeoutCount = 0;
        HeartbeatDeadCount = 0;
        RequestTimeoutCount = 0;
        ErrorCount = 0;
        TooManyRequestsCount = 0;
        EventDropsCount = 0;
        ProcessExitCount = 0;
        RestartExhaustionCount = 0;
        CanStop = false;
        CanRestart = false;
    }

    private string FormatState(PluginDiagnosticsPluginItem item)
    {
        if (item.NodePlugin is null)
        {
            return L("PluginDiagnostics.State.BuiltIn", "Built-in");
        }

        if (!item.IsEnabled)
        {
            return L("PluginDiagnostics.State.Disabled", "Disabled");
        }

        return item.RuntimeSnapshot?.SessionState switch
        {
            SessionState.Starting => L("PluginDiagnostics.State.Starting", "Starting"),
            SessionState.Handshaking => L("PluginDiagnostics.State.Handshaking", "Handshaking"),
            SessionState.Ready => L("PluginDiagnostics.State.Running", "Running"),
            SessionState.Restarting => L("PluginDiagnostics.State.Restarting", "Restarting"),
            SessionState.Stopping => L("PluginDiagnostics.State.Stopping", "Stopping"),
            SessionState.Stopped => item.RuntimeSnapshot?.LastExitCode is not null and not 0
                ? L("PluginDiagnostics.State.Abnormal", "Abnormal")
                : L("PluginDiagnostics.State.Stopped", "Stopped"),
            _ => item.NodePlugin.BusSessionId is null
                ? L("PluginDiagnostics.State.NotStarted", "Not started")
                : L("PluginDiagnostics.State.Running", "Running")
        };
    }

    private string FormatState(PluginDiagnosticsPluginListItem item)
        => item.State;

    private string L(string key, string fallback)
        => _localization.GetCaption(key, fallback);

    private static string FormatDuration(double milliseconds)
        => milliseconds <= 0 ? "-" : $"{milliseconds:0} ms";

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "-";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        var value = bytes.Value;
        var index = 0;
        double scaled = value;
        while (scaled >= 1024 && index < units.Length - 1)
        {
            scaled /= 1024;
            index++;
        }

        return $"{scaled:0.0} {units[index]}";
    }
}

public sealed record FilterOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public sealed class PluginDiagnosticsPluginListItem
{
    public required string IdentityKey { get; init; }
    public required string PluginId { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string RuntimeKind { get; init; }
    public required bool IsEnabled { get; init; }
    public required string State { get; init; }
    public required string SessionId { get; init; }
    public required string Pid { get; init; }
}

public sealed class PluginEndpointRow
{
    public required string EndpointId { get; init; }
    public required string SessionId { get; init; }
    public required string Pending { get; init; }
    public required int PendingHighWater { get; init; }
    public required string Queue { get; init; }
    public required int QueueHighWater { get; init; }
    public required string QueueUsage { get; init; }
    public required string OldestWait { get; init; }
    public required string QueueDrops { get; init; }
    public required string TooManyRequests { get; init; }
    public required string HighPressure { get; init; }
    public required string MaxHighPressure { get; init; }
}

public sealed class PluginCallMetricRow
{
    public required string EndpointId { get; init; }
    public required string Route { get; init; }
    public required string Counts { get; init; }
    public required string Last { get; init; }
    public required string Average { get; init; }
    public required string Max { get; init; }
    public required string P50 { get; init; }
    public required string P95 { get; init; }
    public required string P99 { get; init; }
    public required int RecentSlowCount { get; init; }
}

public sealed class PluginEventMetricRow
{
    public required string EndpointId { get; init; }
    public required string Route { get; init; }
    public required long EventCount { get; init; }
    public required string QueueWaitP50 { get; init; }
    public required string QueueWaitP95 { get; init; }
    public required string QueueWaitP99 { get; init; }
    public required string DeliveryP50 { get; init; }
    public required string DeliveryP95 { get; init; }
    public required string DeliveryP99 { get; init; }
}

public sealed class PluginRecordRow
{
    public required string Timestamp { get; init; }
    public required string Level { get; init; }
    public required string SessionId { get; init; }
    public required string EndpointId { get; init; }
    public required string Route { get; init; }
    public required string CorrelationId { get; init; }
    public required string Message { get; init; }
    public required string Details { get; init; }
}
