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

    [Test]
    public void MajorMismatch_ShouldBeDetected()
    {
        var a = new ProtocolVersion(3, 0);
        var b = new ProtocolVersion(4, 0);

        Assert.That(a.IsMajorCompatibleWith(b), Is.False);
    }

    [Test]
    public void SameMajor_ShouldBeMajorCompatible()
    {
        var a = new ProtocolVersion(3, 0);
        var b = new ProtocolVersion(3, 5);

        Assert.That(a.IsMajorCompatibleWith(b), Is.True);
    }

    [Test]
    public void HighestCommonMinor_ShouldReturnGreatestSharedMinor()
    {
        var ours = new ProtocolVersion(3, 2);
        var theirs = new[] { new ProtocolVersion(3, 0), new ProtocolVersion(3, 1) };

        var result = ours.HighestCommonMinor(theirs);

        Assert.That(result, Is.Not.Null);
        // ReSharper disable once PossibleNullReferenceException
        Assert.That(result!.Value.minor, Is.EqualTo(1));
    }

    [Test]
    public void HighestCommonMinor_ShouldReturnNullWhenNoCommonMinor()
    {
        var ours = new ProtocolVersion(4, 0);
        var theirs = new[] { new ProtocolVersion(3, 0), new ProtocolVersion(3, 1) };

        Assert.That(ours.HighestCommonMinor(theirs), Is.Null);
    }
}
