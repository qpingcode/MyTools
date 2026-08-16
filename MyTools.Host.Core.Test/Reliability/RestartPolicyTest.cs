using System;
using MyTools.Host.Core.Reliability;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Reliability;

[TestFixture]
public class RestartPolicyTest
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public void FirstFailure_ShouldScheduleShortBackoff()
    {
        var policy = new RestartPolicy(
            baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30),
            window: TimeSpan.FromMinutes(5), maxRestartsPerWindow: 5,
            jitter: 0, clock: () => T0);

        var delay = policy.NextDelay();

        Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void RepeatedFailures_ShouldExponentiallyGrowWithinCap()
    {
        var policy = new RestartPolicy(
            baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30),
            window: TimeSpan.FromMinutes(5), maxRestartsPerWindow: 10,
            jitter: 0, clock: () => T0);

        var d1 = policy.NextDelay(); policy.RecordRestart();
        var d2 = policy.NextDelay(); policy.RecordRestart();
        var d3 = policy.NextDelay(); policy.RecordRestart();
        var d4 = policy.NextDelay(); policy.RecordRestart();
        var d5 = policy.NextDelay();

        // 1, 2, 4, 8, 16s
        Assert.That(d1.TotalSeconds, Is.EqualTo(1));
        Assert.That(d2.TotalSeconds, Is.EqualTo(2));
        Assert.That(d3.TotalSeconds, Is.EqualTo(4));
        Assert.That(d4.TotalSeconds, Is.EqualTo(8));
        Assert.That(d5.TotalSeconds, Is.EqualTo(16));
    }

    [Test]
    public void Backoff_ShouldBeCappedAtMaxDelay()
    {
        var policy = new RestartPolicy(
            baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(10),
            window: TimeSpan.FromMinutes(5), maxRestartsPerWindow: 10,
            jitter: 0, clock: () => T0);

        for (var i = 0; i < 6; i++) policy.RecordRestart();
        var delay = policy.NextDelay();

        // base*2^6 = 64s, but capped at 10s.
        Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void CanRestart_BelowWindowLimit_ShouldReturnTrue()
    {
        var policy = new RestartPolicy(
            baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30),
            window: TimeSpan.FromMinutes(5), maxRestartsPerWindow: 3,
            jitter: 0, clock: () => T0);

        policy.RecordRestart();
        policy.RecordRestart();

        Assert.That(policy.CanRestart(), Is.True);
    }

    [Test]
    public void CanRestart_AtWindowLimit_ShouldReturnFalse()
    {
        var policy = new RestartPolicy(
            baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30),
            window: TimeSpan.FromMinutes(5), maxRestartsPerWindow: 3,
            jitter: 0, clock: () => T0);

        policy.RecordRestart();
        policy.RecordRestart();
        policy.RecordRestart();

        Assert.That(policy.CanRestart(), Is.False);
    }

    [Test]
    public void CanRestart_AfterWindowRollsForward_ShouldReturnTrueAgain()
    {
        var now = T0;
        var policy = new RestartPolicy(
            baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30),
            window: TimeSpan.FromMinutes(5), maxRestartsPerWindow: 2,
            jitter: 0, clock: () => now);

        policy.RecordRestart();
        policy.RecordRestart();
        Assert.That(policy.CanRestart(), Is.False);

        // Advance past the window.
        now = now.AddMinutes(6);
        Assert.That(policy.CanRestart(), Is.True);
    }

    [Test]
    public void Jitter_AtRandomMidpoint_ShouldEqualBaseDelay()
    {
        // Jitter factor = 1 + (random - 0.5) * 2 * jitter. With random=0.5 (midpoint) => factor 1.0.
        var policy = new RestartPolicy(
            baseDelay: TimeSpan.FromSeconds(4), maxDelay: TimeSpan.FromSeconds(30),
            window: TimeSpan.FromMinutes(5), maxRestartsPerWindow: 10,
            jitter: 0.25, clock: () => T0, random: _ => 0.5);

        var delay = policy.NextDelay();

        Assert.That(delay.TotalSeconds, Is.EqualTo(4.0).Within(0.01));
    }

    [Test]
    public void Jitter_AtRandomHigh_ShouldAddJitterBand()
    {
        // random=1.0 (max) => factor = 1 + (1 - 0.5)*2*0.25 = 1.25 => 4 * 1.25 = 5s.
        var policy = new RestartPolicy(
            baseDelay: TimeSpan.FromSeconds(4), maxDelay: TimeSpan.FromSeconds(30),
            window: TimeSpan.FromMinutes(5), maxRestartsPerWindow: 10,
            jitter: 0.25, clock: () => T0, random: _ => 1.0);

        var delay = policy.NextDelay();

        Assert.That(delay.TotalSeconds, Is.EqualTo(5.0).Within(0.01));
    }
}
