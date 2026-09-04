using Microsoft.Extensions.Logging;
using MyTools.Host.Core.Sessions;

namespace MyTools.Host.Core.Diagnostics;

public enum PluginCallOutcome
{
    Success,
    Failure,
    Timeout,
    Rejected
}

public sealed record CounterSnapshot(long Total, int Recent);

public sealed record LatencySnapshot(
    long TotalCount,
    int SampleCount,
    double RecentMs,
    double AverageMs,
    double MaxMs,
    double P50Ms,
    double P95Ms,
    double P99Ms);

public sealed record PluginOperationDetailSnapshot(
    long Sequence,
    DateTimeOffset Timestamp,
    string CorrelationId,
    string Route,
    string Outcome,
    double ElapsedMs,
    string? Details);

public sealed record PluginCallMetricsSnapshot(
    string SessionId,
    string EndpointId,
    string Route,
    long CallCount,
    long SuccessCount,
    long FailureCount,
    long TimeoutCount,
    long RejectedCount,
    int RecentSlowCount,
    LatencySnapshot Latency,
    IReadOnlyList<PluginOperationDetailSnapshot> RecentDetails);

public sealed record PluginEventMetricsSnapshot(
    string SessionId,
    string EndpointId,
    string Route,
    long EventCount,
    LatencySnapshot QueueWait,
    LatencySnapshot Delivery);

public sealed record PluginEndpointDiagnosticsSnapshot(
    string SessionId,
    string EndpointId,
    int PendingInFlight,
    int PendingLimit,
    int PendingHighWater,
    int EventQueueDepth,
    int EventQueueCapacity,
    int EventQueueHighWater,
    long EventQueueDroppedTotal,
    int EventQueueDroppedRecent,
    double EventQueueOldestWaitMs,
    double EventQueueUsageRatio,
    double EventQueueHighPressureDurationMs,
    double EventQueueMaxHighPressureDurationMs,
    long TooManyRequestsTotal,
    int TooManyRequestsRecent);

public sealed record NodeProcessResourceUsage(
    int Pid,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    TimeSpan TotalProcessorTime,
    DateTimeOffset SampledAt);

public sealed record NodeProcessSnapshot(
    int Pid,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    double CpuPercent,
    DateTimeOffset SampledAt);

public sealed record NodeProcessExitInfo(
    int? Pid,
    int? ExitCode,
    DateTimeOffset Timestamp,
    string? FailureDetails);

public sealed record PluginDiagnosticRecord(
    long Sequence,
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? PluginId,
    string? SessionId,
    string? EndpointId,
    string? Route,
    string? CorrelationId,
    string? Details);

public sealed record PluginRuntimeDiagnosticsSnapshot(
    string PluginId,
    string? CurrentSessionId,
    SessionState? SessionState,
    int? Pid,
    string? FailureDetails,
    int? LastExitCode,
    CounterSnapshot Disconnects,
    CounterSnapshot Restarts,
    CounterSnapshot HeartbeatTimeouts,
    CounterSnapshot HeartbeatDead,
    CounterSnapshot RequestTimeouts,
    CounterSnapshot Errors,
    CounterSnapshot ProcessExits,
    CounterSnapshot RestartExhaustions,
    CounterSnapshot TooManyRequests,
    CounterSnapshot EventQueueDrops,
    NodeProcessSnapshot? Process,
    IReadOnlyList<PluginEndpointDiagnosticsSnapshot> Endpoints,
    IReadOnlyList<PluginCallMetricsSnapshot> CallMetrics,
    IReadOnlyList<PluginEventMetricsSnapshot> EventMetrics);

public sealed record PluginDiagnosticsSnapshot(
    DateTimeOffset CapturedAt,
    long CurrentSequence,
    IReadOnlyList<PluginRuntimeDiagnosticsSnapshot> Plugins,
    IReadOnlyList<PluginDiagnosticRecord> Records);

public interface IPluginDiagnosticsService
{
    PluginDiagnosticsSnapshot GetSnapshot();

    void RecordDiagnostic(
        LogLevel level,
        string category,
        string message,
        string? pluginId = null,
        string? sessionId = null,
        string? endpointId = null,
        string? route = null,
        string? correlationId = null,
        string? details = null);

    void RecordSessionState(
        string pluginId,
        string sessionId,
        SessionState state,
        int? pid = null,
        string? failureDetails = null);

    void ClearSession(string pluginId, string sessionId, SessionState state, string? failureDetails = null);

    void AttachProcessController(string pluginId, string sessionId, INodeProcessController controller);

    void DetachProcessController(string pluginId, string sessionId, INodeProcessController controller);

    void RecordDisconnect(string pluginId, string sessionId, bool willRestart, string? failureDetails = null);

    void RecordRestart(string pluginId, string previousSessionId, string currentSessionId);

    void RecordRestartExhausted(string pluginId, string sessionId, string? failureDetails = null);

    void RecordHeartbeatTimeout(string pluginId, string sessionId, int consecutiveTimeouts, bool nowDead);

    void RecordProcessExit(string pluginId, string sessionId, NodeProcessExitInfo exitInfo);

    void RecordCallCompleted(
        string pluginId,
        string sessionId,
        string endpointId,
        string route,
        string correlationId,
        double elapsedMs,
        PluginCallOutcome outcome,
        string? details = null);

    void RecordCallTimeout(
        string pluginId,
        string sessionId,
        string endpointId,
        string route,
        string correlationId,
        double elapsedMs,
        string? details = null);

    void RecordCallRejected(
        string pluginId,
        string sessionId,
        string endpointId,
        string route,
        string correlationId,
        string reason);

    void UpdateEndpointPending(
        string pluginId,
        string sessionId,
        string endpointId,
        int inFlight,
        int limit,
        int highWaterMark);

    void RemoveEndpoint(string pluginId, string sessionId, string endpointId);

    void UpdateEventQueueState(
        string pluginId,
        string sessionId,
        string endpointId,
        int depth,
        int capacity,
        int highWaterMark,
        long droppedTotal,
        double oldestWaitMs);

    void RecordEventQueued(
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
        string? droppedRoute = null);

    void RecordEventDelivered(
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
        double oldestWaitMs);
}
