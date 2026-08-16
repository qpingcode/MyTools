using MyTools.Protocol.Errors;

namespace MyTools.Protocol.Routing;

/// <summary>
/// Route namespace groupings, matching the design's §路由规则.
/// Reserved routes (bus.cancel, bus.subscribe, bus.unsubscribe, diagnostics.*) are recognized
/// but return RouteNotFound in Phase 1 because they are not implemented.
/// </summary>
public enum RouteNamespace
{
    Unknown,
    PluginCall,
    HostCall,
    PluginEvent,
    HostEvent,
    Bus,
}

public readonly record struct RouteClassification(bool IsLegal, RouteNamespace Namespace, BusError? Error)
{
    public static RouteClassification Legal(RouteNamespace ns) => new(true, ns, null);
    public static RouteClassification NotFound(string route)
        => new(false, RouteNamespace.Unknown, BusError.For(ErrorCode.RouteNotFound, $"route '{route}' is not implemented"));
}

/// <summary>
/// Classifies a route string against the Phase-1 route namespaces.
/// </summary>
public static class RouteRules
{
    private static readonly HashSet<string> ActiveBusRoutes =
        [Routes.Bus.Handshake, Routes.Bus.Ping];

    private static readonly HashSet<string> ReservedBusRoutes =
        [Routes.Bus.Cancel, Routes.Bus.Subscribe, Routes.Bus.Unsubscribe];

    public static RouteClassification Classify(string route)
    {
        if (string.IsNullOrEmpty(route))
        {
            return RouteClassification.NotFound(route);
        }

        if (ActiveBusRoutes.Contains(route))
        {
            return RouteClassification.Legal(RouteNamespace.Bus);
        }

        if (ReservedBusRoutes.Contains(route))
        {
            return RouteClassification.NotFound(route);
        }

        if (Routes.IsDiagnostics(route))
        {
            return RouteClassification.NotFound(route);
        }

        var ns = ClassifyNamespace(route);
        return ns == RouteNamespace.Unknown
            ? RouteClassification.NotFound(route)
            : RouteClassification.Legal(ns);
    }

    private static RouteNamespace ClassifyNamespace(string route)
    {
        if (Routes.HasSegmentAfterPrefix(route, Routes.Prefix.PluginCall)) return RouteNamespace.PluginCall;
        if (Routes.HasSegmentAfterPrefix(route, Routes.Prefix.HostCall)) return RouteNamespace.HostCall;
        if (Routes.HasSegmentAfterPrefix(route, Routes.Prefix.PluginEvent)) return RouteNamespace.PluginEvent;
        if (Routes.HasSegmentAfterPrefix(route, Routes.Prefix.HostEvent)) return RouteNamespace.HostEvent;
        return RouteNamespace.Unknown;
    }
}
