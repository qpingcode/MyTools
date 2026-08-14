using System.Text;
using MyTools.Protocol.Framing;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Framing;

[TestFixture]
public class FrameDecoderTest
{
    private static byte[] Frame(string json) => FrameCodec.EncodeString(json);

    [Test]
    public void Feed_CompleteFrame_ShouldReturnPayload()
    {
        var dec = new FrameDecoder();

        var result = dec.Feed(Frame("{\"id\":\"1\"}"));

        Assert.That(result.HasFrame, Is.True);
        Assert.That(Encoding.UTF8.GetString(result.Payload!.Value.ToArray()), Is.EqualTo("{\"id\":\"1\"}"));
    }

    [Test]
    public void Feed_PartialPrefix_ShouldWaitForMore()
    {
        var dec = new FrameDecoder();
        var full = Frame("{\"id\":\"1\"}");

        // Feed only first 2 bytes of the 4-byte prefix.
        var r1 = dec.Feed(full.AsSpan(0, 2).ToArray());
        Assert.That(r1.HasFrame, Is.False);

        // Feed the rest.
        var r2 = dec.Feed(full.AsSpan(2).ToArray());
        Assert.That(r2.HasFrame, Is.True);
        Assert.That(Encoding.UTF8.GetString(r2.Payload!.Value.ToArray()), Is.EqualTo("{\"id\":\"1\"}"));
    }

    [Test]
    public void Feed_PayloadSplitAcrossFeeds_ShouldReassemble()
    {
        var dec = new FrameDecoder();
        var full = Frame("{\"hello\":\"world\"}");

        var r1 = dec.Feed(full.AsSpan(0, 6).ToArray());
        Assert.That(r1.HasFrame, Is.False);

        var r2 = dec.Feed(full.AsSpan(6).ToArray());
        Assert.That(r2.HasFrame, Is.True);
        Assert.That(Encoding.UTF8.GetString(r2.Payload!.Value.ToArray()), Is.EqualTo("{\"hello\":\"world\"}"));
    }

    [Test]
    public void Feed_TwoFramesInOneFeed_ShouldReturnFirstThenSecond()
    {
        var dec = new FrameDecoder();
        var combined = Frame("{\"a\":1}").Concat(Frame("{\"b\":2}")).ToArray();

        var r1 = dec.Feed(combined);
        Assert.That(r1.HasFrame, Is.True);
        Assert.That(Encoding.UTF8.GetString(r1.Payload!.Value.ToArray()), Is.EqualTo("{\"a\":1}"));

        var r2 = dec.Feed([]);
        Assert.That(r2.HasFrame, Is.True);
        Assert.That(Encoding.UTF8.GetString(r2.Payload!.Value.ToArray()), Is.EqualTo("{\"b\":2}"));
    }

    [Test]
    public void Feed_ZeroLengthFrame_ShouldReturnEmptyPayload()
    {
        var dec = new FrameDecoder();
        // A zero-length frame: 4 zero prefix bytes.
        var zero = new byte[] { 0, 0, 0, 0 };

        var result = dec.Feed(zero);

        Assert.That(result.HasFrame, Is.True);
        Assert.That(result.Payload!.Value.Length, Is.EqualTo(0));
    }

    [Test]
    public void Feed_LengthExceedingMaxFrameBytes_ShouldReturnErrorWithoutBuffering()
    {
        var dec = new FrameDecoder();
        // Prefix claiming a length 1 byte over the max.
        var over = (long)FrameLimits.MaxFrameBytes + 1;
        var badPrefix = new byte[]
        {
            (byte)(over & 0xFF),
            (byte)((over >> 8) & 0xFF),
            (byte)((over >> 16) & 0xFF),
            (byte)((over >> 24) & 0xFF),
        };

        var result = dec.Feed(badPrefix);

        Assert.That(result.HasFrame, Is.False);
        Assert.That(result.IsFatal, Is.True);
    }

    [Test]
    public void Feed_TruncatedStream_ShouldKeepWaiting()
    {
        var dec = new FrameDecoder();
        var full = Frame("{\"id\":\"1\"}");

        // Feed prefix + half payload, then nothing.
        var r1 = dec.Feed(full.AsSpan(0, 6).ToArray());
        Assert.That(r1.HasFrame, Is.False);
        Assert.That(r1.IsFatal, Is.False);

        // A subsequent empty feed should still wait (not error).
        var r2 = dec.Feed([]);
        Assert.That(r2.HasFrame, Is.False);
        Assert.That(r2.IsFatal, Is.False);
    }

    [Test]
    public void Feed_AfterFatalError_ShouldRemainFatal()
    {
        var dec = new FrameDecoder();
        var over = (long)FrameLimits.MaxFrameBytes + 1;
        var badPrefix = new byte[]
        {
            (byte)(over & 0xFF),
            (byte)((over >> 8) & 0xFF),
            (byte)((over >> 16) & 0xFF),
            (byte)((over >> 24) & 0xFF),
        };

        dec.Feed(badPrefix);
        var r2 = dec.Feed(Frame("{}"));

        Assert.That(r2.IsFatal, Is.True);
        Assert.That(r2.HasFrame, Is.False);
    }
}
