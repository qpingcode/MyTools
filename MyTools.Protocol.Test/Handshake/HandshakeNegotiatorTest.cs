using System.Text.Json.Nodes;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Handshake;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Handshake;

[TestFixture]
public class HandshakeNegotiatorTest
{
    private static readonly ProtocolVersion[] HostSupports =
        [new(3, 0), new(3, 1), new(3, 2)];

    [Test]
    public void Negotiate_MatchingMajor_ShouldPickHighestCommonMinor()
    {
        var nodeSupports = new[] { new ProtocolVersion(3, 0), new ProtocolVersion(3, 1) };

        var result = HandshakeNegotiator.Negotiate(HostSupports, nodeSupports);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Negotiated, Is.EqualTo(new ProtocolVersion(3, 1)));
    }

    [Test]
    public void Negotiate_MajorMismatch_ShouldReturnProtocolMismatch()
    {
        var nodeSupports = new[] { new ProtocolVersion(4, 0) };

        var result = HandshakeNegotiator.Negotiate(HostSupports, nodeSupports);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ProtocolMismatch));
    }

    [Test]
    public void Negotiate_NoCommonMinor_ShouldReturnHandshakeFailed()
    {
        // Host supports 3.x, node supports a different major only.
        var nodeSupports = new[] { new ProtocolVersion(2, 9) };

        var result = HandshakeNegotiator.Negotiate(HostSupports, nodeSupports);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ProtocolMismatch));
    }

    [Test]
    public void Negotiate_ShouldStillProduceProtocolMismatchEvenWhenMajorTooHigh()
    {
        // Even a future major (5.0) must be parseable and return ProtocolMismatch, not throw.
        var nodeSupports = new[] { new ProtocolVersion(5, 0), new ProtocolVersion(5, 1) };

        var result = HandshakeNegotiator.Negotiate(HostSupports, nodeSupports);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ProtocolMismatch));
    }

    [Test]
    public void BuildRequest_ShouldCarryHighestVersionAndAllSupported()
    {
        var payload = HandshakePayload.BuildRequest(HostSupports);

        Assert.That(payload.Version, Is.EqualTo(new ProtocolVersion(3, 2)));
        Assert.That(payload.SupportedVersions, Is.EquivalentTo(HostSupports));
    }

    [Test]
    public void BuildSuccessResponse_ShouldCarryNegotiatedVersion()
    {
        var payload = HandshakePayload.BuildSuccessResponse(new ProtocolVersion(3, 1));

        Assert.That(payload.NegotiatedVersion, Is.EqualTo(new ProtocolVersion(3, 1)));
    }
}
