using MyTools.Host.Core.Sessions;
using MyTools.Protocol.Errors;

namespace MyTools.Host.Core.Bus;

/// <summary>
/// Result of admitting a request into the bus.
/// </summary>
public readonly record struct AdmissionResult(bool IsAdmitted, BusError? Error)
{
    public static AdmissionResult Admit() => new(true, null);
    public static AdmissionResult Reject(BusError e) => new(false, e);
}

/// <summary>
/// Coordinates request admission against session availability and per-hop timeouts.
/// Per the design: requests during non-Ready states return <see cref="ErrorCode.PluginUnavailable"/>
/// (no silent queuing); a per-hop timeout exceeding the route budget returns
/// <see cref="ErrorCode.RequestTimeout"/>.
/// </summary>
public static class RequestAdmission
{
    public static AdmissionResult Check(PluginSessionStateMachine session)
        => session.IsAvailable
            ? AdmissionResult.Admit()
            : AdmissionResult.Reject(BusError.For(ErrorCode.PluginUnavailable,
                $"plugin session is {session.State}, not Ready"));

    public static BusError TimeoutError(int allowedMs, int elapsedMs)
        => BusError.For(ErrorCode.RequestTimeout,
            $"request exceeded per-hop timeout of {allowedMs}ms after {elapsedMs}ms", retryable: false);
}
