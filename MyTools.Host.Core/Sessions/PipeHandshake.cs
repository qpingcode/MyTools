using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Handshake;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Host.Core.Sessions;

/// <summary>
/// Completes the named-pipe <c>bus.handshake</c> exchange on the host side: waits for the Node
/// request and validates the one-shot bootstrap token against the expected process identity.
/// Protocol compatibility is already enforced by manifest validation before process startup.
/// </summary>
public static class PipeHandshake
{
    public static async Task CompleteAsHostAsync(
        IMessageTransport transport,
        BootstrapTokenValidator tokens,
        ProcessIdentity expectedIdentity,
        IIdGenerator ids,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var request = await WaitForHandshakeRequestAsync(transport, timeoutCts.Token);
        var payload = DeserializePayload(request.Payload);

        var observed = expectedIdentity;
        var tokenResult = tokens.Validate(payload.Token ?? "", observed);
        if (!tokenResult.IsValid)
        {
            var fail = BuildErrorReply(request, ids,
                BusError.For(ErrorCode.HandshakeFailed, tokenResult.Reason ?? "token validation failed"));
            await transport.SendAsync(fail, cancellationToken);
            throw new HandshakeException(fail.Error!);
        }

        var reply = new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = ids.NewId(),
            CorrelationId = request.Id,
            TraceId = request.TraceId,
            SessionId = "",
            PluginId = "",
            EndpointId = "",
            Kind = MessageKind.Response,
            Route = Routes.Bus.Handshake,
        };
        await transport.SendAsync(reply, cancellationToken);
    }

    private static async Task<Envelope> WaitForHandshakeRequestAsync(
        IMessageTransport transport, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnMessage(Envelope env)
        {
            if (env.Kind == MessageKind.Request && Routes.IsHandshake(env.Route))
            {
                tcs.TrySetResult(env);
            }
        }

        transport.MessageReceived += OnMessage;
        try
        {
            await using var reg = cancellationToken.Register(() =>
                tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task;
        }
        finally
        {
            transport.MessageReceived -= OnMessage;
        }
    }

    private static HandshakePayload DeserializePayload(JsonNode? payload)
    {
        if (payload is null)
        {
            return new HandshakePayload();
        }

        return payload.Deserialize<HandshakePayload>(ProtocolJsonOptions.Default) ?? new HandshakePayload();
    }

    private static Envelope BuildErrorReply(Envelope request, IIdGenerator ids, BusError error)
        => new()
        {
            Version = ProtocolVersion.Current,
            Id = ids.NewId(),
            CorrelationId = request.Id,
            TraceId = request.TraceId,
            SessionId = "",
            PluginId = "",
            EndpointId = "",
            Kind = MessageKind.Response,
            Route = Routes.Bus.Handshake,
            Error = error,
        };
}

/// <summary>Thrown when the named-pipe handshake fails (token, version, or timeout).</summary>
public sealed class HandshakeException : Exception
{
    public BusError Error { get; }

    public HandshakeException(BusError error)
        : base($"{error.Code}: {error.Message}")
    {
        Error = error;
    }
}
