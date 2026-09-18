using MyTools.Host.Core.Transports;
using MyTools.Protocol.Messages;

namespace MyTools.Host.Core.Bus;

public interface IMessageRouter
{
    void RegisterEndpoint(EndpointId endpoint, IMessageTransport transport);
    void UnregisterEndpoint(EndpointId endpoint);

    Task RouteRequestToNodeAsync(
        Envelope request,
        EndpointId origin,
        CancellationToken cancellationToken = default);

    bool AbandonResponseRoute(string requestId, EndpointId origin);

    Task SendToEndpointAsync(
        EndpointId target,
        Envelope envelope,
        CancellationToken cancellationToken = default);

    Task BroadcastAsync(
        EndpointId sessionEndpoint,
        Envelope envelope,
        string? excludeEndpointId,
        CancellationToken cancellationToken = default);
}

public sealed class MessageRouteException : Exception
{
    public MessageRouteException(MyTools.Protocol.Errors.BusError error)
        : base(error.Message)
    {
        Error = error;
    }

    public MyTools.Protocol.Errors.BusError Error { get; }
}
