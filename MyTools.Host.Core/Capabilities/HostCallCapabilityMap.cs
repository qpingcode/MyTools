using MyTools.Protocol.Routing;

namespace MyTools.Host.Core.Capabilities;

/// <summary>
/// Maps a <c>host.call.*</c> route to the capability id declared in the plugin manifest.
/// Legacy settings methods are folded onto <c>configuration.write</c>; routes that already use
/// capability-shaped names (e.g. <c>host.call.configuration.write</c>) pass through.
/// </summary>
public static class HostCallCapabilityMap
{
    public static string Resolve(string hostCallRoute)
    {
        var method = Routes.StripHostCall(hostCallRoute);

        return method switch
        {
            "getConfiguration" or "saveConfiguration"
                or "getKeymap" or "saveKeymap" or "validateKeymap"
                or "getGestures" or "saveGestures"
                or "suspendGestures" or "resumeGestures"
                or "suspendHotkeys" or "resumeHotkeys" or "checkHotKey"
                or "restart"
                => "configuration.write",
            _ => method,
        };
    }
}
