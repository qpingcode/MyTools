using MyTools.Protocol.Handshake;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Handshake;

[TestFixture]
public class HandshakePayloadTest
{
    [Test]
    public void BuildNamedPipeRequest_ShouldCarryTokenOnly()
    {
        const string token = "bootstrap-token";

        var payload = HandshakePayload.BuildNamedPipeRequest(token);

        Assert.That(payload.Token, Is.EqualTo(token));
    }
}
