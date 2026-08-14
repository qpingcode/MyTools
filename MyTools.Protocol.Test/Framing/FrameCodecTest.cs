using System.Text;
using MyTools.Protocol.Framing;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Framing;

[TestFixture]
public class FrameCodecTest
{
    private static readonly byte[] HelloJson = Encoding.UTF8.GetBytes("{\"id\":\"1\"}");

    [Test]
    public void Encode_ShouldPrependFourByteLittleEndianLength()
    {
        var frame = FrameCodec.Encode(HelloJson);

        // 4-byte LE length prefix + payload.
        Assert.That(frame, Has.Length.EqualTo(4 + HelloJson.Length));
        Assert.That(frame[0], Is.EqualTo(HelloJson.Length)); // length fits in low byte
        Assert.That(frame[1], Is.EqualTo(0));
        Assert.That(frame[2], Is.EqualTo(0));
        Assert.That(frame[3], Is.EqualTo(0));
        Assert.That(frame.AsSpan(4).ToArray(), Is.EqualTo(HelloJson));
    }

    [Test]
    public void Encode_ShouldUseLittleEndianForLargeLength()
    {
        var payload = new byte[300]; // length > 255, needs 2 bytes
        var frame = FrameCodec.Encode(payload);

        Assert.That(frame[0], Is.EqualTo(300 & 0xFF));
        Assert.That(frame[1], Is.EqualTo((300 >> 8) & 0xFF));
        Assert.That(frame[2], Is.EqualTo(0));
        Assert.That(frame[3], Is.EqualTo(0));
    }

    [Test]
    public void EncodeString_ShouldEncodeUtf8Json()
    {
        var frame = FrameCodec.EncodeString("{\"k\":\"v\"}");

        Assert.That(frame, Has.Length.EqualTo(4 + 9)); // {"k":"v"} is 9 bytes
        Assert.That(Encoding.UTF8.GetString(frame.AsSpan(4)), Is.EqualTo("{\"k\":\"v\"}"));
    }

    [Test]
    public void MaxFrameBytes_Default_ShouldBeFourMib()
    {
        Assert.That(FrameLimits.MaxFrameBytes, Is.EqualTo(4 * 1024 * 1024));
    }
}
