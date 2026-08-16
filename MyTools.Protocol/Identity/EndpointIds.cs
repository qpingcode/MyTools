namespace MyTools.Protocol.Identity;

/// <summary>
/// Well-known endpoint labels stamped on envelopes. These are not routes; they identify the
/// transport-bound peer within a session.
/// </summary>
public static class EndpointIds
{
    /// <summary>The plugin's Node process endpoint (one per entry session).</summary>
    public const string NodeMain = "node-main";

    /// <summary>The host-side endpoint used for Host→Node calls and handshake replies.</summary>
    public const string Host = "host";
}
