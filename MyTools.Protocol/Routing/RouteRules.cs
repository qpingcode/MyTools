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
    // Exact-match bus routes implemented in Phase 1.
    private static readonly HashSet<string> ActiveBusRoutes =
        ["bus.handshake", "bus.ping"];

    // Reserved route names — recognized but not implemented in Phase 1 (return RouteNotFound).
    private static readonly HashSet<string> ReservedBusRoutes =
        ["bus.cancel", "bus.subscribe", "bus.unsubscribe"];

    public static RouteClassification Classify(string route)
    {
        if (string.IsNullOrEmpty(route))
        {
            return RouteClassification.NotFound(route);
        }

        // Active bus routes.
        if (ActiveBusRoutes.Contains(route))
        {
            return RouteClassification.Legal(RouteNamespace.Bus);
        }

        // Reserved bus routes: recognized but not implemented.
        if (ReservedBusRoutes.Contains(route))
        {
            return RouteClassification.NotFound(route);
        }

        // Reserved diagnostics.* namespace.
        if (route.StartsWith("diagnostics.", StringComparison.Ordinal))
        {
            return RouteClassification.NotFound(route);
        }

        // Namespaced business routes: must be prefix + '.' + at least one segment.
        var ns = ClassifyNamespace(route);
        return ns == RouteNamespace.Unknown
            ? RouteClassification.NotFound(route)
            : RouteClassification.Legal(ns);
    }

    private static RouteNamespace ClassifyNamespace(string route)
    {
        if (TryPrefix(route, "plugin.call.")) return RouteNamespace.PluginCall;
        if (TryPrefix(route, "host.call.")) return RouteNamespace.HostCall;
        if (TryPrefix(route, "plugin.event.")) return RouteNamespace.PluginEvent;
        if (TryPrefix(route, "host.event.")) return RouteNamespace.HostEvent;
        return RouteNamespace.Unknown;
    }

    private static bool TryPrefix(string route, string prefix)
        => route.Length > prefix.Length && route.StartsWith(prefix, StringComparison.Ordinal);
}
