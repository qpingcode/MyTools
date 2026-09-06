using MyTools.Host.Core.Diagnostics;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Diagnostics;

[TestFixture]
public class BoundedLatencyReservoirTest
{
    [Test]
    public void Snapshot_ShouldKeepBoundedSamplesAndPercentiles()
    {
        var reservoir = new BoundedLatencyReservoir(capacity: 3);

        reservoir.Add(10);
        reservoir.Add(20);
        reservoir.Add(30);
        reservoir.Add(40);

        var snapshot = reservoir.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.TotalCount, Is.EqualTo(4));
            Assert.That(snapshot.SampleCount, Is.EqualTo(3));
            Assert.That(snapshot.RecentMs, Is.EqualTo(40));
            Assert.That(snapshot.AverageMs, Is.EqualTo(25));
            Assert.That(snapshot.MaxMs, Is.EqualTo(40));
            Assert.That(snapshot.P50Ms, Is.EqualTo(30));
            Assert.That(snapshot.P95Ms, Is.EqualTo(40));
            Assert.That(snapshot.P99Ms, Is.EqualTo(40));
        });
    }
}
