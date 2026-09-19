using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Versioning;

[TestFixture]
public class ProtocolVersionTest
{
    [Test]
    public void Current_ShouldBeThreeDotZero()
    {
        Assert.That(ProtocolVersion.Current.Major, Is.EqualTo(3));
        Assert.That(ProtocolVersion.Current.Minor, Is.EqualTo(0));
        Assert.That(ProtocolVersion.Current.ToString(), Is.EqualTo("3.0"));
        Assert.That(ProtocolVersion.CurrentWire, Is.EqualTo("3.0"));
    }

    [Test]
    public void Parse_ShouldReturnMajorAndMinor()
    {
        var v = ProtocolVersion.Parse("3.2");

        Assert.That(v.Major, Is.EqualTo(3));
        Assert.That(v.Minor, Is.EqualTo(2));
    }

    [Test]
    public void Parse_ShouldThrowForMalformedString()
    {
        Assert.That(() => ProtocolVersion.Parse("3"), Throws.ArgumentException);
        Assert.That(() => ProtocolVersion.Parse("abc"), Throws.ArgumentException);
        Assert.That(() => ProtocolVersion.Parse("3.x"), Throws.ArgumentException);
    }

    [Test]
    public void CompareTo_ShouldOrderByVersionNumbers()
    {
        var v30 = new ProtocolVersion(3, 0);
        var v31 = new ProtocolVersion(3, 1);

        Assert.That(v30.CompareTo(v31), Is.LessThan(0));
        Assert.That(v31.CompareTo(v30), Is.GreaterThan(0));
        Assert.That(v30.CompareTo(v30), Is.EqualTo(0));
    }

}
