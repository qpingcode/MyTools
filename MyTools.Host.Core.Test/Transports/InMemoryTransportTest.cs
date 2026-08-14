using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Transports;

[TestFixture]
public class InMemoryTransportTest
{
    private static Envelope Sample(string id, string route) => new()
    {
        Version = ProtocolVersion.Current, Id = id, TraceId = "t", SessionId = "s",
        PluginId = "p", EntryId = "e", EndpointId = "end",
        Kind = MessageKind.Request, Route = route, TimeoutMs = 1000
    };

    [Test]
    public async Task Send_ShouldBeCapturedInSentQueue()
    {
        var t = new InMemoryTransport();

        await t.SendAsync(Sample("1", "plugin.call.x"), CancellationToken.None);

        Assert.That(t.Sent, Has.Count.EqualTo(1));
        Assert.That(t.Sent.ToArray()[0].Id, Is.EqualTo("1"));
    }

    [Test]
    public void Deliver_ShouldRaiseMessageReceived()
    {
        var t = new InMemoryTransport();
        Envelope? received = null;
        t.MessageReceived += e => received = e;

        t.Deliver(Sample("2", "host.call.y"));

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Id, Is.EqualTo("2"));
    }

    [Test]
    public void Disconnect_ShouldFireDisconnectedAndBlockFurtherSend()
    {
        var t = new InMemoryTransport();
        var disconnected = false;
        t.Disconnected += () => disconnected = true;

        t.Disconnect();

        Assert.That(disconnected, Is.True);
        Assert.That(t.IsConnected, Is.False);
        Assert.That(async () => await t.SendAsync(Sample("3", "x"), default),
            Throws.InstanceOf<System.InvalidOperationException>());
    }
}
