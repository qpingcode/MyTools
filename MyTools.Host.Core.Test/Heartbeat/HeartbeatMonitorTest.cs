using System;
using MyTools.Host.Core.Heartbeat;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Heartbeat;

[TestFixture]
public class HeartbeatMonitorTest
{
    private static long Ms(long ms) => ms;

    [Test]
    public void RecordPong_BeforeTimeout_ShouldResetConsecutiveTimeouts()
    {
        var now = Ms(0);
        var m = new HeartbeatMonitor(
            timeoutMs: 5000, deadAfter: 3, clock: () => now);

        m.OnPingSent();          // t=0
        now = Ms(1000);
        m.OnPong();              // pong within timeout
        m.OnPingSent();
        now = Ms(2000);
        m.OnPong();

        Assert.That(m.ConsecutiveTimeouts, Is.EqualTo(0));
        Assert.That(m.IsDead, Is.False);
    }

    [Test]
    public void MissedPing_ShouldIncrementTimeouts()
    {
        var now = Ms(0);
        var m = new HeartbeatMonitor(timeoutMs: 5000, deadAfter: 3, clock: () => now);

        m.OnPingSent();          // t=0
        now = Ms(6000);          // past timeout
        var result = m.CheckTimeout();

        Assert.That(result.TimedOut, Is.True);
        Assert.That(m.ConsecutiveTimeouts, Is.EqualTo(1));
        Assert.That(m.IsDead, Is.False);
    }

    [Test]
    public void DeadAfterConsecutiveTimeouts_ShouldFlagDead()
    {
        var now = Ms(0);
        var m = new HeartbeatMonitor(timeoutMs: 5000, deadAfter: 3, clock: () => now);

        for (var i = 0; i < 3; i++)
        {
            m.OnPingSent();
            now += 6000;
            m.CheckTimeout();
        }

        Assert.That(m.ConsecutiveTimeouts, Is.EqualTo(3));
        Assert.That(m.IsDead, Is.True);
    }

    [Test]
    public void PongAfterMisses_ShouldResetDeadState()
    {
        var now = Ms(0);
        var m = new HeartbeatMonitor(timeoutMs: 5000, deadAfter: 3, clock: () => now);

        // Two misses (not yet dead).
        for (var i = 0; i < 2; i++)
        {
            m.OnPingSent();
            now += 6000;
            m.CheckTimeout();
        }
        Assert.That(m.IsDead, Is.False);

        // A successful pong resets everything.
        m.OnPong();
        Assert.That(m.ConsecutiveTimeouts, Is.EqualTo(0));
        Assert.That(m.IsDead, Is.False);
    }

    [Test]
    public void LastRtt_ShouldBeRecorded()
    {
        var now = Ms(0);
        var m = new HeartbeatMonitor(timeoutMs: 5000, deadAfter: 3, clock: () => now);

        m.OnPingSent();
        now = Ms(1200);
        m.OnPong();

        Assert.That(m.LastRttMs, Is.EqualTo(1200));
    }
}
