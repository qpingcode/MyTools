using System.Text;

namespace MyTools.Protocol.Framing;

/// <summary>
/// Encodes/decodes length-prefixed frames for the named-pipe transport.
/// Wire format: [4-byte little-endian unsigned length][UTF-8 JSON payload].
/// </summary>
public static class FrameCodec
{
    /// <summary>Encodes a raw payload byte array into a length-prefixed frame.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        var frame = new byte[FrameLimits.PrefixBytes + payload.Length];
        var length = payload.Length;
        frame[0] = (byte)(length & 0xFF);
        frame[1] = (byte)((length >> 8) & 0xFF);
        frame[2] = (byte)((length >> 16) & 0xFF);
        frame[3] = (byte)((length >> 24) & 0xFF);
        payload.CopyTo(frame.AsSpan(FrameLimits.PrefixBytes));
        return frame;
    }

    /// <summary>Encodes a string (UTF-8 JSON) into a length-prefixed frame.</summary>
    public static byte[] EncodeString(string json)
        => Encode(Encoding.UTF8.GetBytes(json));
}
