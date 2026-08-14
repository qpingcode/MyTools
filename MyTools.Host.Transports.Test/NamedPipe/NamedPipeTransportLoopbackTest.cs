using System.Text.Json;
using System.Threading.Tasks;
using MyTools.Host.Transports.NamedPipe;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Framing;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Host.Transports.Test.NamedPipe;

[TestFixture]
public class NamedPipeTransportLoopbackTest
{
    private static Envelope Sample(string id) => new()
    {
        Version = ProtocolVersion.Current, Id = id, TraceId = id, SessionId = "s",
        PluginId = "p", EntryId = "e", EndpointId = "end",
        Kind = MessageKind.Request, Route = "plugin.call.x", TimeoutMs = 1000
    };

    [Test]
    public async Task ClientToServer_ShouldDeliverEnvelopeWrittenByRawClient()
    {
        // Raw client writes a length-prefixed frame; the server transport's read loop surfaces it.
        var pipeName = $"mytools-test-{System.Guid.NewGuid():N}";
        await using var server = new NamedPipeTransport(pipeName, isServer: true);
        var serverConnect = server.ConnectAsync(default);

        // Start the server's WaitForConnection, then connect a raw client concurrently.
        await using var client = new System.IO.Pipes.NamedPipeClientStream(
            ".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        await serverConnect;

        Envelope? received = null;
        server.MessageReceived += e => received = e;

        var json = JsonSerializer.Serialize(Sample("in-1"), ProtocolJsonOptions.Default);
        await client.WriteAsync(FrameCodec.EncodeString(json));
        await client.FlushAsync();

        for (var i = 0; i < 50 && received is null; i++) await Task.Delay(10);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Id, Is.EqualTo("in-1"));
    }

    [Test]
    public async Task ServerToClient_ShouldDeliverFrameWrittenByTransport()
    {
        // Both ends are NamedPipeTransport: the client side runs its own read loop, so the frame
        // written by the server surfaces via the client's MessageReceived event. Using a raw
        // ReadExactlyAsync here would race the transport's read loop for the same pipe.
        var pipeName = $"mytools-test-{System.Guid.NewGuid():N}";
        await using var server = new NamedPipeTransport(pipeName, isServer: true);
        await using var client = new NamedPipeTransport(pipeName, isServer: false);

        var connectServer = server.ConnectAsync(default);
        var connectClient = client.ConnectAsync(default);
        await Task.WhenAll(connectServer, connectClient);

        Envelope? received = null;
        client.MessageReceived += e => received = e;

        await server.SendAsync(Sample("out-1"), default);

        for (var i = 0; i < 50 && received is null; i++)
        {
            await Task.Delay(10);
        }

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Id, Is.EqualTo("out-1"));
    }
}
