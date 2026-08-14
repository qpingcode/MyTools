using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Protocol.Messages;

namespace MyTools.Host.Core.Transports;

/// <summary>
/// A test double for <see cref="IMessageTransport"/> representing one side of a connection between
/// the bus and a remote endpoint (Node or WebView). The bus writes via <see cref="SendAsync"/>
/// (captured in <see cref="Sent"/>); the test simulates the remote endpoint by calling
/// <see cref="Deliver"/> to raise <see cref="MessageReceived"/>. This mirrors real transports: a
/// named-pipe/WebView2 transport is the bus's view of one connection.
/// </summary>
public sealed class InMemoryTransport : IMessageTransport
{
    private readonly ConcurrentQueue<Envelope> _sent = new();
    private bool _connected = true;

    public bool IsConnected => _connected;

    /// <summary>Envelopes the bus has written via <see cref="SendAsync"/> (what the remote would receive).</summary>
    public ConcurrentQueue<Envelope> Sent => _sent;

    public event Action<Envelope>? MessageReceived;
    public event Action? Disconnected;

    public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        if (!_connected)
        {
            throw new InvalidOperationException("transport is disconnected");
        }
        _sent.Enqueue(envelope);
        return ValueTask.CompletedTask;
    }

    /// <summary>Simulates the remote endpoint sending an envelope; raises <see cref="MessageReceived"/>.</summary>
    public void Deliver(Envelope env) => MessageReceived?.Invoke(env);

    public void Disconnect()
    {
        if (!_connected) return;
        _connected = false;
        Disconnected?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        return ValueTask.CompletedTask;
    }
}
