using System.Collections.Generic;
using System.Threading.Tasks;
using MyTools.Host.Core.Sessions;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Sessions;

[TestFixture]
public class SessionActorTest
{
    [Test]
    public async Task Post_ShouldExecuteActionsInOrder()
    {
        var actor = new SessionActor();
        var order = new List<int>();

        await actor.PostAsync(() => order.Add(1));
        await actor.PostAsync(() => order.Add(2));
        await actor.PostAsync(() => order.Add(3));

        Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Post_ConcurrentSubmissions_ShouldStillRunSerially()
    {
        var actor = new SessionActor();
        var log = new List<int>();
        var tasks = new List<Task>();

        for (var i = 0; i < 50; i++)
        {
            var captured = i;
            tasks.Add(Task.Run(async () => await actor.PostAsync(() => log.Add(captured))));
        }
        await Task.WhenAll(tasks);

        // All 50 ran, none lost, and each value appears exactly once.
        Assert.That(log, Has.Count.EqualTo(50));
        Assert.That(log, Is.Unique);
    }

    [Test]
    public async Task Post_ShouldNotRunConcurrently()
    {
        // A guard that would deadlock if two actions ever overlapped.
        var actor = new SessionActor();
        var inFlight = 0;
        var maxInFlight = 0;

        var tasks = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (var j = 0; j < 5; j++)
                {
                    await actor.PostAsync(() =>
                    {
                        inFlight++;
                        maxInFlight = System.Math.Max(maxInFlight, inFlight);
                        inFlight--;
                    });
                }
            }));
        }
        await Task.WhenAll(tasks);

        Assert.That(maxInFlight, Is.EqualTo(1));
    }
}
