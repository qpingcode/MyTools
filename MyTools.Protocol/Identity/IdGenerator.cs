namespace MyTools.Protocol.Identity;

/// <summary>
/// Generates globally-unique string identifiers for envelope fields
/// (<c>id</c>, <c>traceId</c>, <c>sessionId</c>, <c>endpointId</c>).
/// </summary>
public interface IIdGenerator
{
    string NewId();
}

/// <summary>
/// Default implementation using 32-character lowercase-hex GUIDs (no dashes).
/// The design only requires global uniqueness; GUIDs satisfy that with zero external dependencies.
/// </summary>
public sealed class GuidIdGenerator : IIdGenerator
{
    public string NewId() => Guid.NewGuid().ToString("N");
}
