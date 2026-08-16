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
/// request, validates the one-shot bootstrap token against the expected process identity,
/// negotiates the protocol version, and replies with the bound session identity.
/// </summary>
public static class PipeHandshake
{
    public static readonly ProtocolVersion[] HostSupportedVersions = [ProtocolVersion.Current];

    public static async Task<ProtocolVersion> CompleteAsHostAsync(
        IMessageTransport transport,
        BootstrapTokenValidator tokens,
        ProcessIdentity expectedIdentity,
        string sessionId,
        string endpointId,
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

        var theirs = payload.SupportedVersions is { Count: > 0 }
            ? payload.SupportedVersions
            : payload.Version is { } single ? new[] { single } : Array.Empty<ProtocolVersion>();
        var negotiated = HandshakeNegotiator.Negotiate(HostSupportedVersions, theirs);
        if (!negotiated.IsSuccess)
        {
            var fail = BuildErrorReply(request, ids, negotiated.Error!);
            await transport.SendAsync(fail, cancellationToken);
            throw new HandshakeException(negotiated.Error!);
        }

        var successPayload = HandshakePayload.BuildSuccessResponse(
            negotiated.Negotiated!.Value,
            expectedIdentity.PluginId,
            expectedIdentity.EntryId,
            sessionId,
            endpointId);
        var reply = new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = ids.NewId(),
            CorrelationId = request.Id,
            TraceId = request.TraceId,
            SessionId = sessionId,
            PluginId = expectedIdentity.PluginId,
            EntryId = expectedIdentity.EntryId,
            EndpointId = EndpointIds.Host,
            Kind = MessageKind.Response,
            Route = Routes.Bus.Handshake,
            Payload = JsonSerializer.SerializeToNode(successPayload, ProtocolJsonOptions.Default),
        };
        await transport.SendAsync(reply, cancellationToken);
        return negotiated.Negotiated!.Value;
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
            SessionId = request.SessionId,
            PluginId = request.PluginId,
            EntryId = request.EntryId,
            EndpointId = EndpointIds.Host,
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
