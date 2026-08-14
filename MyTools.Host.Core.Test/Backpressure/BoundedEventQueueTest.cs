using System.Linq;
using MyTools.Host.Core.Backpressure;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Backpressure;

[TestFixture]
public class BoundedEventQueueTest
{
    private static int E(int n) => n;

    [Test]
    public void Enqueue_BelowCapacity_ShouldRetainAll()
    {
        var q = new BoundedEventQueue<int>(capacity: 3);
        q.Enqueue(E(1));
        q.Enqueue(E(2));

        Assert.That(q.Drain(), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(q.DroppedEvents, Is.EqualTo(0));
    }

    [Test]
    public void Enqueue_AtCapacity_ShouldDropOldest()
    {
        var q = new BoundedEventQueue<int>(capacity: 2);
        q.Enqueue(E(1));
        q.Enqueue(E(2));
        q.Enqueue(E(3)); // capacity exceeded -> drop oldest (1)

        Assert.That(q.Drain(), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(q.DroppedEvents, Is.EqualTo(1));
    }

    [Test]
    public void Enqueue_ManyOverflows_ShouldAccumulateDroppedCount()
    {
        var q = new BoundedEventQueue<int>(capacity: 2);
        foreach (var n in Enumerable.Range(1, 10))
        {
            q.Enqueue(n);
        }

        Assert.That(q.Drain(), Is.EqualTo(new[] { 9, 10 }));
        Assert.That(q.DroppedEvents, Is.EqualTo(8));
    }

    [Test]
    public void Drain_AfterDrain_ShouldBeEmpty()
    {
        var q = new BoundedEventQueue<int>(capacity: 5);
        q.Enqueue(1);
        q.Enqueue(2);

        q.Drain();
        var second = q.Drain();

        Assert.That(second, Is.Empty);
    }
}
