using System;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Protocol.Messages;

namespace MyTools.Host.Core.Transports;

/// <summary>
/// A connected transport carrying protocol envelopes between two endpoints. Implementations are
/// responsible for connection, frame send/receive and disconnect notification — not for business
/// routing (that is the bus's job). Each transport is bound to fixed plugin/entry/endpoint identity
/// at creation; the page/peer cannot declare or switch identity.
/// </summary>
public interface IMessageTransport : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Asynchronously sends an envelope to the peer. Completes when the frame is written.</summary>
    ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken);

    /// <summary>Raised on the receiver thread (or pump) when a complete envelope arrives.</summary>
    event Action<Envelope>? MessageReceived;

    /// <summary>Raised when the connection drops (either side or transport failure).</summary>
    event Action? Disconnected;
}
