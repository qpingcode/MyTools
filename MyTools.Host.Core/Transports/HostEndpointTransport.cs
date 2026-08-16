using System;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Protocol.Messages;

namespace MyTools.Host.Core.Transports;

/// <summary>
/// Transport representing the host as a bus endpoint. The bus writes responses/events destined for
/// the host via <see cref="SendAsync"/>; those are forwarded to <see cref="Delivered"/> so the host
/// correlator can complete pending <c>plugin.call.*</c> requests and raise events — without a
/// second subscription on the Node pipe.
/// </summary>
public sealed class HostEndpointTransport : IMessageTransport
{
    public bool IsConnected { get; private set; } = true;

    /// <summary>Raised when the bus delivers an envelope to the host endpoint.</summary>
    public event Action<Envelope>? Delivered;

    public event Action<Envelope>? MessageReceived
    {
        add { }
        remove { }
    }

    public event Action? Disconnected;

    public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("host endpoint transport is disconnected");
        }

        Delivered?.Invoke(envelope);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!IsConnected) return ValueTask.CompletedTask;
        IsConnected = false;
        Disconnected?.Invoke();
        return ValueTask.CompletedTask;
    }
}
