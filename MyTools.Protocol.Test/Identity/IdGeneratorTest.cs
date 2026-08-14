using MyTools.Protocol.Identity;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Identity;

[TestFixture]
public class IdGeneratorTest
{
    [Test]
    public void NewId_ShouldReturnNonEmptyString()
    {
        IIdGenerator gen = new GuidIdGenerator();

        var id = gen.NewId();

        Assert.That(id, Is.Not.Null);
        Assert.That(id, Is.Not.Empty);
    }

    [Test]
    public void NewId_ShouldDifferFromConsecutiveCall()
    {
        IIdGenerator gen = new GuidIdGenerator();

        var first = gen.NewId();
        var second = gen.NewId();

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void NewId_ShouldBeThirtyTwoHexCharsWithoutDashes()
    {
        IIdGenerator gen = new GuidIdGenerator();

        var id = gen.NewId();

        Assert.That(id, Has.Length.EqualTo(32));
        Assert.That(id, Does.Match("^[0-9a-f]+$"));
    }
}
