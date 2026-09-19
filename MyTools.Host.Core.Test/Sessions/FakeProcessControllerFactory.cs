using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Diagnostics;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;

namespace MyTools.Host.Core.Test.Sessions;

/// <summary>
/// Fake process controller for session-manager tests: StartAsync immediately provides an
/// InMemoryTransport so the manager can register the Node endpoint without spawning a process.
/// </summary>
internal sealed class FakeProcessController : INodeProcessController
{
    public IMessageTransport? Transport { get; private set; }
    public ProcessIdentity? ObservedIdentity { get; private set; }
    public string? FailureDetails { get; set; }
    public NodeProcessResourceUsage? ResourceUsage { get; set; }
    public event Action<NodeProcessExitInfo>? ProcessExited;

    public Task StartAsync(
        string pipeName,
        string pluginId,
        CancellationToken cancellationToken)
    {
        Transport = new InMemoryTransport();
        ObservedIdentity = new ProcessIdentity(
            Pid: 4242,
            CreationTime: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PluginId: pluginId);
        return Task.CompletedTask;
    }

    public Task StopAsync() => Task.CompletedTask;

    public NodeProcessResourceUsage? TryGetResourceUsage() => ResourceUsage;

    public void RaiseExited(int? exitCode = null)
    {
        ProcessExited?.Invoke(new NodeProcessExitInfo(
            ObservedIdentity?.Pid,
            exitCode,
            DateTimeOffset.UtcNow,
            FailureDetails));
    }
}

internal sealed class FakeProcessControllerFactory : INodeProcessControllerFactory
{
    public FakeProcessController? LastController { get; private set; }

    public INodeProcessController Create(string nodeExePath, string nodeEntryFullPath)
        => LastController = new FakeProcessController();
}
