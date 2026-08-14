using MyTools.Host.Core.Sessions;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Sessions;

[TestFixture]
public class GenerationGuardTest
{
    [Test]
    public void NewGeneration_ShouldBeCurrent()
    {
        var guard = new GenerationGuard();
        var token = guard.Current;

        Assert.That(guard.IsCurrent(token), Is.True);
    }

    [Test]
    public void AfterBump_OldToken_ShouldNoLongerBeCurrent()
    {
        var guard = new GenerationGuard();
        var old = guard.Current;

        guard.Bump();

        Assert.That(guard.IsCurrent(old), Is.False);
        Assert.That(guard.IsCurrent(guard.Current), Is.True);
    }

    [Test]
    public void Bump_ShouldIncrementGeneration()
    {
        var guard = new GenerationGuard();
        Assert.That(guard.Generation, Is.EqualTo(0));

        guard.Bump();
        Assert.That(guard.Generation, Is.EqualTo(1));

        guard.Bump();
        Assert.That(guard.Generation, Is.EqualTo(2));
    }

    [Test]
    public void MultipleBumps_OnlyLatestIsCurrent()
    {
        var guard = new GenerationGuard();
        var t0 = guard.Current;
        guard.Bump();
        var t1 = guard.Current;
        guard.Bump();
        var t2 = guard.Current;

        Assert.That(guard.IsCurrent(t0), Is.False);
        Assert.That(guard.IsCurrent(t1), Is.False);
        Assert.That(guard.IsCurrent(t2), Is.True);
    }
}
