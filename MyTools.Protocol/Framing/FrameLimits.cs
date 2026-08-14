namespace MyTools.Protocol.Framing;

/// <summary>
/// Global frame size limits. Named-pipe frames are 4-byte little-endian unsigned length prefix
/// followed by UTF-8 JSON. <see cref="MaxFrameBytes"/> is the hard global ceiling; routes may set
/// lower payload limits but never higher.
/// </summary>
public static class FrameLimits
{
    /// <summary>4 MiB hard ceiling on a single frame's payload (excluding the 4-byte prefix).</summary>
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    /// <summary>4-byte little-endian length prefix size.</summary>
    public const int PrefixBytes = 4;
}
