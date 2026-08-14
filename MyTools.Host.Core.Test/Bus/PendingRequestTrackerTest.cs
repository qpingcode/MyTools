using MyTools.Host.Core.Backpressure;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Backpressure;

[TestFixture]
public class PendingRequestTrackerTest
{
    [Test]
    public void DefaultLimit_ShouldBeSixtyFour()
    {
        var tracker = new PendingRequestTracker();

        Assert.That(tracker.Limit, Is.EqualTo(64));
    }

    [Test]
    public void TryReserve_BelowLimit_ShouldSucceed()
    {
        var tracker = new PendingRequestTracker(limit: 3);

        Assert.That(tracker.TryReserve("r1", "plugin.call.x"), Is.True);
        Assert.That(tracker.TryReserve("r2", "plugin.call.y"), Is.True);
        Assert.That(tracker.InFlight, Is.EqualTo(2));
    }

    [Test]
    public void TryReserve_AtLimit_ShouldFail()
    {
        var tracker = new PendingRequestTracker(limit: 2);
        tracker.TryReserve("r1", "plugin.call.x");
        tracker.TryReserve("r2", "plugin.call.y");

        Assert.That(tracker.TryReserve("r3", "plugin.call.z"), Is.False);
        Assert.That(tracker.InFlight, Is.EqualTo(2));
    }

    [Test]
    public void Release_ShouldFreeSlot()
    {
        var tracker = new PendingRequestTracker(limit: 1);
        Assert.That(tracker.TryReserve("r1", "plugin.call.x"), Is.True);
        Assert.That(tracker.TryReserve("r2", "plugin.call.y"), Is.False);

        tracker.Release("r1", "plugin.call.x");

        Assert.That(tracker.TryReserve("r3", "plugin.call.z"), Is.True);
    }

    [Test]
    public void TryReserve_PingRoute_ShouldNotConsumeSlot()
    {
        var tracker = new PendingRequestTracker(limit: 1);
        tracker.TryReserve("r1", "plugin.call.x");

        // bus.ping is exempt and admitted even when the limit is reached.
        Assert.That(tracker.TryReserve("ping1", "bus.ping"), Is.True);
        Assert.That(tracker.InFlight, Is.EqualTo(1));
    }

    [Test]
    public void ReleasePing_ShouldNotDecrementBelowZero()
    {
        var tracker = new PendingRequestTracker(limit: 4);
        tracker.Release("ping1", "bus.ping"); // never reserved; should be a no-op
        Assert.That(tracker.InFlight, Is.EqualTo(0));
    }
}
