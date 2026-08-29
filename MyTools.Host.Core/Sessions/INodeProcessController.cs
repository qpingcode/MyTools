using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Transports;

namespace MyTools.Host.Core.Sessions;

/// <summary>
/// Controls the lifecycle of the Node child process for one plugin: spawn it with the bootstrap
/// token on stdin, expose its transport once the named pipe is connected, and report lifecycle
/// outcomes. The real implementation (in MyTools.Host.Transports) spawns `node`; tests use a fake.
/// </summary>
public interface INodeProcessController
{
    /// <summary>
    /// Starts the Node process and brings up the named-pipe transport. After the process is
    /// created (or immediately for fakes), <paramref name="issueToken"/> is invoked with the
    /// observed <see cref="ProcessIdentity"/> and must return the one-shot token value written to
    /// stdin. Completes when the transport is connected (handshake then proceeds on the bus).
    /// </summary>
    Task StartAsync(
        string pipeName,
        string pluginId,
        Func<ProcessIdentity, string> issueToken,
        CancellationToken cancellationToken);

    /// <summary>The transport once connected; null before StartAsync completes.</summary>
    IMessageTransport? Transport { get; }

    /// <summary>Observed process identity after start; null before StartAsync completes.</summary>
    ProcessIdentity? ObservedIdentity { get; }

    /// <summary>Terminates the process tree (the whole Job Object).</summary>
    Task StopAsync();
}

public interface INodeProcessControllerFactory
{
    INodeProcessController Create(string nodeExePath, string nodeEntryFullPath);
}
