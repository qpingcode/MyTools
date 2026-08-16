using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Handshake;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Versioning;

namespace MyTools.Host.Core.Test.Sessions;

/// <summary>
/// Fake process controller for session-manager tests: StartAsync immediately provides an
/// InMemoryTransport and delivers a valid <c>bus.handshake</c> request (as the Node SDK would),
/// so the manager can complete handshake without spawning a real process.
/// </summary>
internal sealed class FakeProcessController : INodeProcessController
{
    public IMessageTransport? Transport { get; private set; }
    public ProcessIdentity? ObservedIdentity { get; private set; }

    public Task StartAsync(
        string pipeName,
        string pluginId,
        string entryId,
        Func<ProcessIdentity, string> issueToken,
        CancellationToken cancellationToken)
    {
        var transport = new InMemoryTransport();
        Transport = transport;
        ObservedIdentity = new ProcessIdentity(
            Pid: 4242,
            CreationTime: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PluginId: pluginId,
            EntryId: entryId);
        var token = issueToken(ObservedIdentity);

        var payload = HandshakePayload.BuildNamedPipeRequest(PipeHandshake.HostSupportedVersions, token);
        transport.Deliver(new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = Guid.NewGuid().ToString("N"),
            TraceId = Guid.NewGuid().ToString("N"),
            SessionId = "",
            PluginId = pluginId,
            EntryId = entryId,
            EndpointId = "node-main",
            Kind = MessageKind.Request,
            Route = "bus.handshake",
            TimeoutMs = 5000,
            Payload = JsonSerializer.SerializeToNode(payload, ProtocolJsonOptions.Default),
        });

        return Task.CompletedTask;
    }

    public Task StopAsync() => Task.CompletedTask;
}

internal sealed class FakeProcessControllerFactory : INodeProcessControllerFactory
{
    public INodeProcessController Create(string nodeExePath, string nodeEntryFullPath)
        => new FakeProcessController();
}
