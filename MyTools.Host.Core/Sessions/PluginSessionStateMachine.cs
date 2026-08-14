using System;
using System.Collections.Generic;

namespace MyTools.Host.Core.Sessions;

/// <summary>
/// Plugin session lifecycle states per the design state machine:
/// <code>
/// Created -&gt; Starting -&gt; Handshaking -&gt; Ready
///               |             |           |
///               +-------------+-----------+-&gt; Restarting -&gt; Starting
///                                            |
///                                            v
///                                          Stopped
/// Created / Starting / Handshaking / Ready / Restarting -&gt; Stopping -&gt; Stopped
/// </code>
/// </summary>
public enum SessionState
{
    Created,
    Starting,
    Handshaking,
    Ready,
    Restarting,
    Stopping,
    Stopped,
}

/// <summary>
/// Enforces legal transitions between <see cref="SessionState"/> values. Illegal transitions throw
/// <see cref="InvalidOperationException"/>. Stopping/Stopped is terminal-ish: from Stopped nothing
/// else is allowed.
/// </summary>
public sealed class PluginSessionStateMachine
{
    // Per-state set of legal target states.
    private static readonly Dictionary<SessionState, HashSet<SessionState>> LegalTransitions = new()
    {
        [SessionState.Created] = [SessionState.Starting, SessionState.Stopping],
        [SessionState.Starting] = [SessionState.Handshaking, SessionState.Restarting, SessionState.Stopping],
        [SessionState.Handshaking] = [SessionState.Ready, SessionState.Restarting, SessionState.Stopping],
        [SessionState.Ready] = [SessionState.Restarting, SessionState.Stopping],
        [SessionState.Restarting] = [SessionState.Starting, SessionState.Stopping],
        [SessionState.Stopping] = [SessionState.Stopped],
        [SessionState.Stopped] = [],
    };

    public SessionState State { get; private set; } = SessionState.Created;

    public bool IsAvailable => State == SessionState.Ready;

    public void Transition(SessionState target)
    {
        if (!LegalTransitions.TryGetValue(State, out var legal) || !legal.Contains(target))
        {
            throw new InvalidOperationException(
                $"illegal session transition: {State} -&gt; {target}");
        }
        State = target;
    }
}
