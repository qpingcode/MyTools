namespace MyTools.Protocol.Routing;

/// <summary>
/// Canonical wire strings for v3 routes. Closed bus names and well-known plugin.call methods live
/// here; open namespaces (<c>plugin.call.*</c>, <c>host.call.*</c>, …) are prefix + helper.
/// <see cref="RouteRules"/> classifies against this catalog — callers must not re-hardcode the same
/// strings.
/// </summary>
public static class Routes
{
    public static class Bus
    {
        public const string Handshake = "bus.handshake";
        public const string Ping = "bus.ping";
        public const string Cancel = "bus.cancel";
        public const string Subscribe = "bus.subscribe";
        public const string Unsubscribe = "bus.unsubscribe";
    }

    public static class Prefix
    {
        public const string PluginCall = "plugin.call.";
        public const string HostCall = "host.call.";
        public const string PluginEvent = "plugin.event.";
        public const string HostEvent = "host.event.";
        public const string Diagnostics = "diagnostics.";
    }

    /// <summary>Host↔Node methods frozen by the v3 SDK / <c>INodePluginHost</c> surface.</summary>
    public static class PluginCall
    {
        public const string Initialize = Prefix.PluginCall + "initialize";
        public const string Search = Prefix.PluginCall + "search";
        public const string InvokeAction = Prefix.PluginCall + "invokeAction";
        public const string DetailEvent = Prefix.PluginCall + "detailEvent";
        public const string DetailCall = Prefix.PluginCall + "detailCall";

        public static string Of(string method)
            => StartsWithPrefix(method, Prefix.PluginCall) ? method : Prefix.PluginCall + method;
    }

    public static class HostCall
    {
        public static string Of(string method)
            => StartsWithPrefix(method, Prefix.HostCall) ? method : Prefix.HostCall + method;
    }

    public static class PluginEvent
    {
        public static string Of(string subjectId)
            => StartsWithPrefix(subjectId, Prefix.PluginEvent) ? subjectId : Prefix.PluginEvent + subjectId;
    }

    public static bool IsPing(string route) => route == Bus.Ping;
    public static bool IsHandshake(string route) => route == Bus.Handshake;
    public static bool IsHostCall(string route) => StartsWithPrefix(route, Prefix.HostCall);
    public static bool IsPluginCall(string route) => StartsWithPrefix(route, Prefix.PluginCall);
    public static bool IsPluginEvent(string route) => StartsWithPrefix(route, Prefix.PluginEvent);
    public static bool IsHostEvent(string route) => StartsWithPrefix(route, Prefix.HostEvent);
    public static bool IsDiagnostics(string route) => StartsWithPrefix(route, Prefix.Diagnostics);

    /// <summary>
    /// True when <paramref name="route"/> has at least one segment after <paramref name="prefix"/>
    /// (e.g. <c>plugin.call.save</c>, not <c>plugin.call.</c> or <c>plugin.call</c>).
    /// </summary>
    public static bool HasSegmentAfterPrefix(string route, string prefix)
        => route.Length > prefix.Length && route.StartsWith(prefix, StringComparison.Ordinal);

    public static string StripHostCall(string route)
        => StartsWithPrefix(route, Prefix.HostCall) ? route[Prefix.HostCall.Length..] : route;

    public static bool StartsWithPrefix(string route, string prefix)
        => route.StartsWith(prefix, StringComparison.Ordinal);
}
