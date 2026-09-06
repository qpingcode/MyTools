using MyTools.Host.Core.Capabilities;

namespace MyTools.Host.Core.Sessions;

/// <summary>
/// One running attempt of a plugin: identity, lifecycle state and generation. The manager
/// drives the state machine and bumps the generation on each restart so stale callbacks are
/// discarded. Endpoints (Node + WebViews) are registered on the MessageBus by the manager.
/// </summary>
public sealed class PluginSession
{
    public string PluginId { get; }
    public string SessionId { get; }
    public int Generation { get; internal set; }

    private readonly PluginSessionStateMachine _sm = new();
    private readonly GenerationGuard _gen = new();

    public PluginSession(string pluginId, string sessionId)
    {
        PluginId = pluginId;
        SessionId = sessionId;
    }

    public SessionState State => _sm.State;
    public bool IsAvailable => _sm.IsAvailable;
    internal GenerationGuard GenerationGuard => _gen;

    /// <summary>The process controller owning the Node child process; set by the manager on start.</summary>
    public INodeProcessController? Controller { get; internal set; }

    /// <summary>Disconnect handler wired by the manager; cleared on tear-down.</summary>
    internal Action? DisconnectHandler { get; set; }

    /// <summary>Process exit handler wired by the manager; cleared on tear-down.</summary>
    internal Action<Diagnostics.NodeProcessExitInfo>? ProcessExitHandler { get; set; }

    internal void Transition(SessionState target)
    {
        _sm.Transition(target);
        if (target == SessionState.Starting)
        {
            _gen.Bump();
            Generation = _gen.Generation;
        }
    }
}
