using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Host.Core.Sessions;

namespace MyTools.Host.Core.Diagnostics;

public sealed class PluginDiagnosticsService : IPluginDiagnosticsService, IDisposable
{
    private const int DefaultRecordCapacity = 500;
    private const int DefaultDetailCapacity = 32;
    private const double SlowCallThresholdMs = 2_000;
    private static readonly TimeSpan DefaultRecentWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultProcessSampleInterval = TimeSpan.FromSeconds(2);
    private const double QueueHighPressureRatio = 0.80;

    private readonly ConcurrentDictionary<string, PluginState> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly BoundedBuffer<PluginDiagnosticRecord> _records = new(DefaultRecordCapacity);
    private readonly ILogger<PluginDiagnosticsService> _logger;
    private readonly CancellationTokenSource _samplingCts = new();
    private readonly Task _samplingTask;
    private long _sequence;

    public PluginDiagnosticsService(ILogger<PluginDiagnosticsService>? logger = null)
    {
        _logger = logger ?? NullLogger<PluginDiagnosticsService>.Instance;
        _samplingTask = Task.Run(SampleProcessesAsync);
    }

    public PluginDiagnosticsSnapshot GetSnapshot()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var plugins = _plugins.Values
            .Select(state => state.ToSnapshot(capturedAt))
            .OrderBy(state => state.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var records = _records.Snapshot()
            .OrderBy(record => record.Timestamp)
            .ThenBy(record => record.Sequence)
            .ToArray();

        return new PluginDiagnosticsSnapshot(capturedAt, Interlocked.Read(ref _sequence), plugins, records);
    }

    public void RecordDiagnostic(
        LogLevel level,
        string category,
        string message,
        string? pluginId = null,
        string? sessionId = null,
        string? endpointId = null,
        string? route = null,
        string? correlationId = null,
        string? details = null)
    {
        var record = CreateRecord(level, category, message, pluginId, sessionId, endpointId, route, correlationId, details);
        _records.Add(record);
        _logger.Log(level,
            "Plugin diagnostic category={category} pluginId={pluginId} sessionId={sessionId} endpointId={endpointId} route={route} correlationId={correlationId} sequence={sequence} message={message} details={details}",
            category, pluginId, sessionId, endpointId, route, correlationId, record.Sequence, message, details);
    }

    public void RecordSessionState(
        string pluginId,
        string sessionId,
        SessionState state,
        int? pid = null,
        string? failureDetails = null)
    {
        var plugin = GetOrCreate(pluginId);
        plugin.SetSessionState(sessionId, state, pid, failureDetails);
        RecordDiagnostic(LogLevel.Information,
            "session.state",
            $"Session state changed to {state}.",
            pluginId,
            sessionId,
            details: failureDetails);
    }

    public void ClearSession(string pluginId, string sessionId, SessionState state, string? failureDetails = null)
    {
        var plugin = GetOrCreate(pluginId);
        plugin.ClearSession(sessionId, state, failureDetails);
    }

    public void AttachProcessController(string pluginId, string sessionId, INodeProcessController controller)
    {
        GetOrCreate(pluginId).AttachController(sessionId, controller);
    }

    public void DetachProcessController(string pluginId, string sessionId, INodeProcessController controller)
    {
        if (_plugins.TryGetValue(pluginId, out var plugin))
        {
            plugin.DetachController(sessionId, controller);
        }
    }

    public void RecordDisconnect(string pluginId, string sessionId, bool willRestart, string? failureDetails = null)
    {
        var plugin = GetOrCreate(pluginId);
        plugin.Disconnects.Increment();
        plugin.FailureDetails = failureDetails ?? plugin.FailureDetails;
        RecordDiagnostic(LogLevel.Warning,
            "session.disconnect",
            willRestart ? "Node session disconnected; restart scheduled." : "Node session disconnected; restart budget exhausted.",
            pluginId,
            sessionId,
            details: failureDetails);
    }

    public void RecordRestart(string pluginId, string previousSessionId, string currentSessionId)
    {
        var plugin = GetOrCreate(pluginId);
        plugin.Restarts.Increment();
        RecordDiagnostic(LogLevel.Warning,
            "session.restart",
            $"Node session restarted from {previousSessionId} to {currentSessionId}.",
            pluginId,
            currentSessionId,
            details: $"previousSessionId={previousSessionId}");
    }

    public void RecordRestartExhausted(string pluginId, string sessionId, string? failureDetails = null)
    {
        var plugin = GetOrCreate(pluginId);
        plugin.RestartExhaustions.Increment();
        plugin.FailureDetails = failureDetails ?? plugin.FailureDetails;
        RecordDiagnostic(LogLevel.Error,
            "session.restart.exhausted",
            "Automatic restart budget exhausted; plugin stopped.",
            pluginId,
            sessionId,
            details: failureDetails);
    }

    public void RecordHeartbeatTimeout(string pluginId, string sessionId, int consecutiveTimeouts, bool nowDead)
    {
        var plugin = GetOrCreate(pluginId);
        plugin.HeartbeatTimeouts.Increment();
        RecordDiagnostic(nowDead ? LogLevel.Error : LogLevel.Warning,
            nowDead ? "heartbeat.dead" : "heartbeat.timeout",
            nowDead
                ? $"Heartbeat declared dead after {consecutiveTimeouts} consecutive timeouts."
                : $"Heartbeat timeout {consecutiveTimeouts}.",
            pluginId,
            sessionId);

        if (nowDead)
        {
            plugin.HeartbeatDead.Increment();
        }
    }

    public void RecordProcessExit(string pluginId, string sessionId, NodeProcessExitInfo exitInfo)
    {
        var plugin = GetOrCreate(pluginId);
        plugin.ProcessExits.Increment();
        plugin.LastExitCode = exitInfo.ExitCode;
        plugin.FailureDetails = exitInfo.FailureDetails ?? plugin.FailureDetails;
        RecordDiagnostic(
            exitInfo.ExitCode is null or 0 ? LogLevel.Information : LogLevel.Error,
            "process.exit",
            exitInfo.ExitCode is null
                ? "Node process exited."
                : $"Node process exited with code {exitInfo.ExitCode}.",
            pluginId,
            sessionId,
            details: exitInfo.FailureDetails);
    }

    public void RecordCallCompleted(
        string pluginId,
        string sessionId,
        string endpointId,
        string route,
        string correlationId,
        double elapsedMs,
        PluginCallOutcome outcome,
        string? details = null)
    {
        var plugin = GetOrCreate(pluginId);
        var metric = plugin.GetCallMetric(sessionId, endpointId, route);
        var isSlow = elapsedMs >= SlowCallThresholdMs;
        PluginOperationDetailSnapshot? detail = null;

        switch (outcome)
        {
            case PluginCallOutcome.Success:
                break;
            case PluginCallOutcome.Failure:
                plugin.Errors.Increment();
                break;
        }

        if (isSlow)
        {
            detail = CreateDetail(
                route,
                correlationId,
                outcome.ToString().ToLowerInvariant(),
                elapsedMs,
                details);
        }
        else if (outcome == PluginCallOutcome.Failure)
        {
            detail = CreateDetail(
                route,
                correlationId,
                "failure",
                elapsedMs,
                details);
        }

        metric.RecordCompleted(elapsedMs, outcome, isSlow, detail);

        if (isSlow)
        {
            RecordDiagnostic(
                LogLevel.Warning,
                "call.slow",
                $"Slow call {route} completed in {elapsedMs:0} ms.",
                pluginId,
                sessionId,
                endpointId,
                route,
                correlationId,
                details);
            return;
        }

        if (outcome == PluginCallOutcome.Failure)
        {
            RecordDiagnostic(
                LogLevel.Warning,
                "call.failure",
                $"Call {route} failed after {elapsedMs:0} ms.",
                pluginId,
                sessionId,
                endpointId,
                route,
                correlationId,
                details);
        }
    }

    public void RecordCallTimeout(
        string pluginId,
        string sessionId,
        string endpointId,
        string route,
        string correlationId,
        double elapsedMs,
        string? details = null)
    {
        var plugin = GetOrCreate(pluginId);
        var metric = plugin.GetCallMetric(sessionId, endpointId, route);
        metric.RecordTimeout(
            elapsedMs,
            elapsedMs >= SlowCallThresholdMs,
            CreateDetail(route, correlationId, "timeout", elapsedMs, details));
        plugin.RequestTimeouts.Increment();
        RecordDiagnostic(
            LogLevel.Warning,
            "call.timeout",
            $"Call {route} timed out after {elapsedMs:0} ms.",
            pluginId,
            sessionId,
            endpointId,
            route,
            correlationId,
            details);
    }

    public void RecordCallRejected(
        string pluginId,
        string sessionId,
        string endpointId,
        string route,
        string correlationId,
        string reason)
    {
        var plugin = GetOrCreate(pluginId);
        var metric = plugin.GetCallMetric(sessionId, endpointId, route);
        metric.RecordRejected(CreateDetail(route, correlationId, "rejected", 0, reason));
        plugin.TooManyRequests.Increment();
        plugin.GetEndpoint(sessionId, endpointId).RecordTooManyRequest();
        RecordDiagnostic(
            LogLevel.Warning,
            "call.rejected",
            $"Call {route} rejected with TooManyRequests.",
            pluginId,
            sessionId,
            endpointId,
            route,
            correlationId,
            reason);
    }

    public void UpdateEndpointPending(
        string pluginId,
        string sessionId,
        string endpointId,
        int inFlight,
        int limit,
        int highWaterMark)
    {
        GetOrCreate(pluginId)
            .GetEndpoint(sessionId, endpointId)
            .UpdatePending(inFlight, limit, highWaterMark);
    }

    public void RemoveEndpoint(string pluginId, string sessionId, string endpointId)
    {
        if (_plugins.TryGetValue(pluginId, out var plugin))
        {
            plugin.RemoveEndpoint(sessionId, endpointId);
        }
    }

    public void UpdateEventQueueState(
        string pluginId,
        string sessionId,
        string endpointId,
        int depth,
        int capacity,
        int highWaterMark,
        long droppedTotal,
        double oldestWaitMs)
    {
        GetOrCreate(pluginId)
            .GetEndpoint(sessionId, endpointId)
            .UpdateQueue(depth, capacity, highWaterMark, droppedTotal, oldestWaitMs);
    }

    public void RecordEventQueued(
        string pluginId,
        string sessionId,
        string endpointId,
        string route,
        int depth,
        int capacity,
        int highWaterMark,
        long droppedTotal,
        bool dropped,
        double oldestWaitMs,
        string? droppedRoute = null)
    {
        var plugin = GetOrCreate(pluginId);
        var endpoint = plugin.GetEndpoint(sessionId, endpointId);
        endpoint.UpdateQueue(depth, capacity, highWaterMark, droppedTotal, oldestWaitMs);
        plugin.GetEventMetric(sessionId, endpointId, route).RecordQueued();

        if (!dropped)
        {
            return;
        }

        plugin.EventQueueDrops.Increment();
        RecordDiagnostic(
            LogLevel.Warning,
            "event.drop",
            $"Event queue dropped the oldest item while enqueuing {route}.",
            pluginId,
            sessionId,
            endpointId,
            droppedRoute ?? route,
            details: $"droppedTotal={droppedTotal}");
    }

    public void RecordEventDelivered(
        string pluginId,
        string sessionId,
        string endpointId,
        string route,
        double queueWaitMs,
        double deliveryMs,
        int depth,
        int capacity,
        int highWaterMark,
        long droppedTotal,
        double oldestWaitMs)
    {
        var plugin = GetOrCreate(pluginId);
        var endpoint = plugin.GetEndpoint(sessionId, endpointId);
        endpoint.UpdateQueue(depth, capacity, highWaterMark, droppedTotal, oldestWaitMs);
        plugin.GetEventMetric(sessionId, endpointId, route).RecordDelivered(queueWaitMs, deliveryMs);
    }

    public void Dispose()
    {
        _samplingCts.Cancel();
        try
        {
            _samplingTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore best-effort shutdown
        }
        _samplingCts.Dispose();
    }

    private async Task SampleProcessesAsync()
    {
        using var timer = new PeriodicTimer(DefaultProcessSampleInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_samplingCts.Token))
            {
                foreach (var plugin in _plugins.Values)
                {
                    plugin.SampleProcess();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private PluginDiagnosticRecord CreateRecord(
        LogLevel level,
        string category,
        string message,
        string? pluginId,
        string? sessionId,
        string? endpointId,
        string? route,
        string? correlationId,
        string? details)
    {
        return new PluginDiagnosticRecord(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            level,
            category,
            message,
            pluginId,
            sessionId,
            endpointId,
            route,
            correlationId,
            details);
    }

    private PluginOperationDetailSnapshot CreateDetail(
        string route,
        string correlationId,
        string outcome,
        double elapsedMs,
        string? details)
    {
        return new PluginOperationDetailSnapshot(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            correlationId,
            route,
            outcome,
            elapsedMs,
            details);
    }

    private PluginState GetOrCreate(string pluginId)
        => _plugins.GetOrAdd(pluginId,
            id => new PluginState(id, DefaultRecentWindow, DefaultDetailCapacity));

    private sealed class PluginState
    {
        public string PluginId { get; }
        private readonly object _gate = new();
        private readonly TimeSpan _recentWindow;
        private readonly int _detailCapacity;
        private readonly Dictionary<string, EndpointState> _endpoints = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CallMetricState> _callMetrics = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EventMetricState> _eventMetrics = new(StringComparer.Ordinal);
        private ProcessBinding? _controllerBinding;
        private NodeProcessResourceUsage? _lastResourceUsage;

        public PluginState(string pluginId, TimeSpan recentWindow, int detailCapacity)
        {
            PluginId = pluginId;
            _recentWindow = recentWindow;
            _detailCapacity = detailCapacity;
            Disconnects = new RollingCounter(recentWindow);
            Restarts = new RollingCounter(recentWindow);
            HeartbeatTimeouts = new RollingCounter(recentWindow);
            HeartbeatDead = new RollingCounter(recentWindow);
            RequestTimeouts = new RollingCounter(recentWindow);
            Errors = new RollingCounter(recentWindow);
            ProcessExits = new RollingCounter(recentWindow);
            RestartExhaustions = new RollingCounter(recentWindow);
            TooManyRequests = new RollingCounter(recentWindow);
            EventQueueDrops = new RollingCounter(recentWindow);
        }

        public string? CurrentSessionId { get; private set; }
        public SessionState? SessionState { get; private set; }
        public int? Pid { get; private set; }
        public string? FailureDetails { get; set; }
        public int? LastExitCode { get; set; }
        public NodeProcessSnapshot? Process { get; private set; }

        public RollingCounter Disconnects { get; }
        public RollingCounter Restarts { get; }
        public RollingCounter HeartbeatTimeouts { get; }
        public RollingCounter HeartbeatDead { get; }
        public RollingCounter RequestTimeouts { get; }
        public RollingCounter Errors { get; }
        public RollingCounter ProcessExits { get; }
        public RollingCounter RestartExhaustions { get; }
        public RollingCounter TooManyRequests { get; }
        public RollingCounter EventQueueDrops { get; }

        public void SetSessionState(string sessionId, SessionState state, int? pid, string? failureDetails)
        {
            lock (_gate)
            {
                if (!string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
                {
                    CurrentSessionId = sessionId;
                    _endpoints.Clear();
                    _callMetrics.Clear();
                    _eventMetrics.Clear();
                    _lastResourceUsage = null;
                    Process = null;
                }

                SessionState = state;
                if (pid.HasValue)
                {
                    Pid = pid.Value;
                }

                if (!string.IsNullOrWhiteSpace(failureDetails))
                {
                    FailureDetails = failureDetails;
                }
            }
        }

        public void ClearSession(string sessionId, SessionState state, string? failureDetails)
        {
            lock (_gate)
            {
                if (string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
                {
                    SessionState = state;
                    if (!string.IsNullOrWhiteSpace(failureDetails))
                    {
                        FailureDetails = failureDetails;
                    }

                    _controllerBinding = null;
                }
            }
        }

        public void AttachController(string sessionId, INodeProcessController controller)
        {
            lock (_gate)
            {
                if (!string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
                {
                    CurrentSessionId = sessionId;
                }

                _controllerBinding = new ProcessBinding(sessionId, controller);
            }
        }

        public void DetachController(string sessionId, INodeProcessController controller)
        {
            lock (_gate)
            {
                if (_controllerBinding is { SessionId: var current, Controller: var attached }
                    && string.Equals(current, sessionId, StringComparison.Ordinal)
                    && ReferenceEquals(attached, controller))
                {
                    _controllerBinding = null;
                }
            }
        }

        public void SampleProcess()
        {
            ProcessBinding? binding;
            lock (_gate)
            {
                binding = _controllerBinding;
            }

            if (binding?.Controller.TryGetResourceUsage() is not { } sample)
            {
                return;
            }

            lock (_gate)
            {
                Pid = sample.Pid;
                if (_lastResourceUsage is null)
                {
                    Process = new NodeProcessSnapshot(sample.Pid, sample.WorkingSetBytes, sample.PrivateMemoryBytes, 0, sample.SampledAt);
                    _lastResourceUsage = sample;
                    return;
                }

                var elapsedMs = (sample.SampledAt - _lastResourceUsage.SampledAt).TotalMilliseconds;
                var cpuMs = (sample.TotalProcessorTime - _lastResourceUsage.TotalProcessorTime).TotalMilliseconds;
                var cpuPercent = elapsedMs <= 0
                    ? 0
                    : Math.Max(0, Math.Min(100, cpuMs / (elapsedMs * Environment.ProcessorCount) * 100));
                Process = new NodeProcessSnapshot(
                    sample.Pid,
                    sample.WorkingSetBytes,
                    sample.PrivateMemoryBytes,
                    cpuPercent,
                    sample.SampledAt);
                _lastResourceUsage = sample;
            }
        }

        public EndpointState GetEndpoint(string sessionId, string endpointId)
        {
            lock (_gate)
            {
                return _endpoints.GetValueOrDefault(EndpointKey(sessionId, endpointId))
                       ?? (_endpoints[EndpointKey(sessionId, endpointId)] = new EndpointState(_recentWindow));
            }
        }

        public void RemoveEndpoint(string sessionId, string endpointId)
        {
            lock (_gate)
            {
                _endpoints.Remove(EndpointKey(sessionId, endpointId));
            }
        }

        public CallMetricState GetCallMetric(string sessionId, string endpointId, string route)
        {
            lock (_gate)
            {
                return _callMetrics.GetValueOrDefault(MetricKey(sessionId, endpointId, route))
                       ?? (_callMetrics[MetricKey(sessionId, endpointId, route)] = new CallMetricState(_recentWindow, _detailCapacity));
            }
        }

        public EventMetricState GetEventMetric(string sessionId, string endpointId, string route)
        {
            lock (_gate)
            {
                return _eventMetrics.GetValueOrDefault(MetricKey(sessionId, endpointId, route))
                       ?? (_eventMetrics[MetricKey(sessionId, endpointId, route)] = new EventMetricState());
            }
        }

        public PluginRuntimeDiagnosticsSnapshot ToSnapshot(DateTimeOffset now)
        {
            lock (_gate)
            {
                var endpoints = _endpoints
                    .Select(entry => entry.Value.ToSnapshot(entry.Key, now))
                    .OrderBy(entry => entry.EndpointId, StringComparer.Ordinal)
                    .ToArray();
                var calls = _callMetrics
                    .Select(entry => entry.Value.ToSnapshot(entry.Key))
                    .OrderBy(entry => entry.EndpointId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Route, StringComparer.Ordinal)
                    .ToArray();
                var events = _eventMetrics
                    .Select(entry => entry.Value.ToSnapshot(entry.Key))
                    .OrderBy(entry => entry.EndpointId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Route, StringComparer.Ordinal)
                    .ToArray();

                return new PluginRuntimeDiagnosticsSnapshot(
                    PluginId,
                    CurrentSessionId,
                    SessionState,
                    Pid,
                    FailureDetails,
                    LastExitCode,
                    Disconnects.Snapshot(),
                    Restarts.Snapshot(),
                    HeartbeatTimeouts.Snapshot(),
                    HeartbeatDead.Snapshot(),
                    RequestTimeouts.Snapshot(),
                    Errors.Snapshot(),
                    ProcessExits.Snapshot(),
                    RestartExhaustions.Snapshot(),
                    TooManyRequests.Snapshot(),
                    EventQueueDrops.Snapshot(),
                    Process,
                    endpoints,
                    calls,
                    events);
            }
        }

        private static string EndpointKey(string sessionId, string endpointId) => $"{sessionId}\u001f{endpointId}";
        private static string MetricKey(string sessionId, string endpointId, string route) => $"{sessionId}\u001f{endpointId}\u001f{route}";
    }

    private sealed class EndpointState
    {
        private readonly object _gate = new();
        private int _pendingInFlight;
        private int _pendingLimit;
        private int _pendingHighWater;
        private int _eventQueueDepth;
        private int _eventQueueCapacity;
        private int _eventQueueHighWater;
        private long _eventQueueDroppedTotal;
        private double _eventQueueOldestWaitMs;
        private DateTimeOffset? _highPressureSince;
        private double _maxHighPressureDurationMs;

        public EndpointState(TimeSpan recentWindow)
        {
            TooManyRequests = new RollingCounter(recentWindow);
            DroppedEvents = new RollingCounter(recentWindow);
        }

        public RollingCounter TooManyRequests { get; }
        public RollingCounter DroppedEvents { get; }

        public void RecordTooManyRequest()
        {
            lock (_gate)
            {
                TooManyRequests.Increment();
            }
        }

        public void UpdatePending(int inFlight, int limit, int highWaterMark)
        {
            lock (_gate)
            {
                _pendingInFlight = inFlight;
                _pendingLimit = limit;
                _pendingHighWater = highWaterMark;
            }
        }

        public void UpdateQueue(int depth, int capacity, int highWaterMark, long droppedTotal, double oldestWaitMs)
        {
            lock (_gate)
            {
                _eventQueueDepth = depth;
                _eventQueueCapacity = capacity;
                _eventQueueHighWater = highWaterMark;
                if (droppedTotal > _eventQueueDroppedTotal)
                {
                    var delta = droppedTotal - _eventQueueDroppedTotal;
                    for (var i = 0; i < delta; i++)
                    {
                        DroppedEvents.Increment();
                    }
                }

                _eventQueueDroppedTotal = droppedTotal;
                _eventQueueOldestWaitMs = oldestWaitMs;

                var now = DateTimeOffset.UtcNow;
                var highPressure = capacity > 0 && depth >= Math.Ceiling(capacity * QueueHighPressureRatio);
                if (highPressure)
                {
                    _highPressureSince ??= now;
                    _maxHighPressureDurationMs = Math.Max(_maxHighPressureDurationMs, (now - _highPressureSince.Value).TotalMilliseconds);
                }
                else if (_highPressureSince is not null)
                {
                    _maxHighPressureDurationMs = Math.Max(_maxHighPressureDurationMs, (now - _highPressureSince.Value).TotalMilliseconds);
                    _highPressureSince = null;
                }
            }
        }

        public PluginEndpointDiagnosticsSnapshot ToSnapshot(string key, DateTimeOffset now)
        {
            lock (_gate)
            {
                var separator = key.IndexOf('\u001f');
                var sessionId = separator < 0 ? string.Empty : key[..separator];
                var endpointId = separator < 0 ? key : key[(separator + 1)..];
                var highPressureDurationMs = _highPressureSince is null
                    ? 0
                    : (now - _highPressureSince.Value).TotalMilliseconds;
                var droppedEvents = DroppedEvents.Snapshot();
                var tooManyRequests = TooManyRequests.Snapshot();

                return new PluginEndpointDiagnosticsSnapshot(
                    sessionId,
                    endpointId,
                    _pendingInFlight,
                    _pendingLimit,
                    _pendingHighWater,
                    _eventQueueDepth,
                    _eventQueueCapacity,
                    _eventQueueHighWater,
                    _eventQueueDroppedTotal,
                    droppedEvents.Recent,
                    _eventQueueOldestWaitMs,
                    _eventQueueCapacity <= 0 ? 0 : (double)_eventQueueDepth / _eventQueueCapacity,
                    highPressureDurationMs,
                    Math.Max(_maxHighPressureDurationMs, highPressureDurationMs),
                    tooManyRequests.Total,
                    tooManyRequests.Recent);
            }
        }
    }

    private sealed class CallMetricState
    {
        private readonly object _gate = new();
        private long _callCount;
        private long _successCount;
        private long _failureCount;
        private long _timeoutCount;
        private long _rejectedCount;

        public CallMetricState(TimeSpan recentWindow, int detailCapacity)
        {
            SlowCalls = new RollingCounter(recentWindow);
            Latency = new BoundedLatencyReservoir();
            RecentDetails = new BoundedBuffer<PluginOperationDetailSnapshot>(detailCapacity);
        }

        public RollingCounter SlowCalls { get; }
        public BoundedLatencyReservoir Latency { get; }
        public BoundedBuffer<PluginOperationDetailSnapshot> RecentDetails { get; }

        public void RecordCompleted(
            double elapsedMs,
            PluginCallOutcome outcome,
            bool isSlow,
            PluginOperationDetailSnapshot? detail)
        {
            lock (_gate)
            {
                _callCount++;
                Latency.Add(elapsedMs);
                switch (outcome)
                {
                    case PluginCallOutcome.Success:
                        _successCount++;
                        break;
                    case PluginCallOutcome.Failure:
                        _failureCount++;
                        break;
                }

                if (isSlow)
                {
                    SlowCalls.Increment();
                }

                if (detail is not null)
                {
                    RecentDetails.Add(detail);
                }
            }
        }

        public void RecordTimeout(double elapsedMs, bool isSlow, PluginOperationDetailSnapshot detail)
        {
            lock (_gate)
            {
                _callCount++;
                _timeoutCount++;
                Latency.Add(elapsedMs);
                if (isSlow)
                {
                    SlowCalls.Increment();
                }

                RecentDetails.Add(detail);
            }
        }

        public void RecordRejected(PluginOperationDetailSnapshot detail)
        {
            lock (_gate)
            {
                _callCount++;
                _rejectedCount++;
                RecentDetails.Add(detail);
            }
        }

        public PluginCallMetricsSnapshot ToSnapshot(string key)
        {
            lock (_gate)
            {
                var (sessionId, endpointId, route) = SplitMetricKey(key);
                return new PluginCallMetricsSnapshot(
                    sessionId,
                    endpointId,
                    route,
                    _callCount,
                    _successCount,
                    _failureCount,
                    _timeoutCount,
                    _rejectedCount,
                    SlowCalls.Snapshot().Recent,
                    Latency.Snapshot(),
                    RecentDetails.Snapshot()
                        .OrderBy(detail => detail.Timestamp)
                        .ThenBy(detail => detail.Sequence)
                        .ToArray());
            }
        }
    }

    private sealed class EventMetricState
    {
        private readonly object _gate = new();
        private long _eventCount;
        public BoundedLatencyReservoir QueueWait { get; } = new();
        public BoundedLatencyReservoir Delivery { get; } = new();

        public void RecordQueued()
        {
            lock (_gate)
            {
                _eventCount++;
            }
        }

        public void RecordDelivered(double queueWaitMs, double deliveryMs)
        {
            lock (_gate)
            {
                QueueWait.Add(queueWaitMs);
                Delivery.Add(deliveryMs);
            }
        }

        public PluginEventMetricsSnapshot ToSnapshot(string key)
        {
            lock (_gate)
            {
                var (sessionId, endpointId, route) = SplitMetricKey(key);
                return new PluginEventMetricsSnapshot(
                    sessionId,
                    endpointId,
                    route,
                    _eventCount,
                    QueueWait.Snapshot(),
                    Delivery.Snapshot());
            }
        }
    }

    private sealed class RollingCounter
    {
        private readonly object _gate = new();
        private readonly TimeSpan _window;
        private readonly Queue<DateTimeOffset> _recent = new();
        private long _total;

        public RollingCounter(TimeSpan window)
        {
            _window = window;
        }

        public void Increment(DateTimeOffset? at = null)
        {
            lock (_gate)
            {
                var timestamp = at ?? DateTimeOffset.UtcNow;
                _total++;
                _recent.Enqueue(timestamp);
                Trim(timestamp);
            }
        }

        public CounterSnapshot Snapshot(DateTimeOffset? now = null)
        {
            lock (_gate)
            {
                Trim(now ?? DateTimeOffset.UtcNow);
                return new CounterSnapshot(_total, _recent.Count);
            }
        }

        private void Trim(DateTimeOffset now)
        {
            while (_recent.Count > 0 && now - _recent.Peek() > _window)
            {
                _recent.Dequeue();
            }
        }
    }

    private sealed class BoundedBuffer<T>
    {
        private readonly object _gate = new();
        private readonly int _capacity;
        private readonly Queue<T> _items = new();

        public BoundedBuffer(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            _capacity = capacity;
        }

        public void Add(T item)
        {
            lock (_gate)
            {
                _items.Enqueue(item);
                while (_items.Count > _capacity)
                {
                    _items.Dequeue();
                }
            }
        }

        public IReadOnlyList<T> Snapshot()
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    private sealed record ProcessBinding(string SessionId, INodeProcessController Controller);

    private static (string SessionId, string EndpointId, string Route) SplitMetricKey(string key)
    {
        var parts = key.Split('\u001f');
        return (parts.ElementAtOrDefault(0) ?? string.Empty,
            parts.ElementAtOrDefault(1) ?? string.Empty,
            parts.ElementAtOrDefault(2) ?? string.Empty);
    }
}
