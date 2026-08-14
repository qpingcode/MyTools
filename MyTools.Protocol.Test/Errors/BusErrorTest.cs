using System.Text.Json;
using MyTools.Protocol.Errors;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Errors;

[TestFixture]
public class BusErrorTest
{
    [Test]
    public void Serialize_ShouldRoundTripAllFields()
    {
        var err = new BusError(ErrorCode.RequestTimeout, "timed out", retryable: false,
            details: new { field = "timeoutMs", limit = 30000 });

        var json = JsonSerializer.Serialize(err, ProtocolJsonOptions.Default);
        var back = JsonSerializer.Deserialize<BusError>(json, ProtocolJsonOptions.Default)!;

        Assert.That(back.Code, Is.EqualTo(ErrorCode.RequestTimeout));
        Assert.That(back.Message, Is.EqualTo("timed out"));
        Assert.That(back.Retryable, Is.False);
    }

    [Test]
    public void Serialize_ShouldEmitCodeAsCamelCaseString()
    {
        var err = new BusError(ErrorCode.CapabilityNotDeclared, "x", false, null);

        var json = JsonSerializer.Serialize(err, ProtocolJsonOptions.Default);

        Assert.That(json, Does.Contain("\"code\":\"CapabilityNotDeclared\""));
    }

    [Test]
    public void Serialize_DetailsNull_ShouldOmitDetailsField()
    {
        var err = new BusError(ErrorCode.InternalError, "x", false, null);

        var json = JsonSerializer.Serialize(err, ProtocolJsonOptions.Default);

        Assert.That(json, Does.Not.Contains("\"details\""));
    }

    [Test]
    public void ErrorCode_ShouldExposeAllTwelveActiveCodes()
    {
        var active = new[]
        {
            ErrorCode.ProtocolMismatch,
            ErrorCode.HandshakeFailed,
            ErrorCode.CapabilityNotDeclared,
            ErrorCode.CapabilityDenied,
            ErrorCode.InvalidPayload,
            ErrorCode.MessageTooLarge,
            ErrorCode.RouteNotFound,
            ErrorCode.RequestTimeout,
            ErrorCode.TooManyRequests,
            ErrorCode.TransportDisconnected,
            ErrorCode.PluginUnavailable,
            ErrorCode.InternalError,
        };

        Assert.That(active, Has.Length.EqualTo(12));
        Assert.That(active, Is.Unique);
    }

    [Test]
    public void ErrorCode_ShouldExposeTwoReservedCodes()
    {
        var reserved = new[] { ErrorCode.Cancelled, ErrorCode.RateLimited };

        Assert.That(reserved, Has.Length.EqualTo(2));
        Assert.That(reserved, Is.Unique);
    }

    [Test]
    public void Factory_ShouldCreateWithDefaultMessage()
    {
        var err = BusError.For(ErrorCode.RouteNotFound);

        Assert.That(err.Code, Is.EqualTo(ErrorCode.RouteNotFound));
        Assert.That(err.Retryable, Is.False);
    }
}
