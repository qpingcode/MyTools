using MyTools.Host.Core.Sessions;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Sessions;

[TestFixture]
public class PluginSessionStateMachineTest
{
    [Test]
    public void New_ShouldBeInCreatedState()
    {
        var sm = new PluginSessionStateMachine();
        Assert.That(sm.State, Is.EqualTo(SessionState.Created));
    }

    [Test]
    public void Transition_CreatedToStarting_ShouldSucceed()
    {
        var sm = new PluginSessionStateMachine();
        sm.Transition(SessionState.Starting);
        Assert.That(sm.State, Is.EqualTo(SessionState.Starting));
    }

    [Test]
    public void HappyPath_CreatedStartingHandshakingReady()
    {
        var sm = new PluginSessionStateMachine();
        sm.Transition(SessionState.Starting);
        sm.Transition(SessionState.Handshaking);
        sm.Transition(SessionState.Ready);

        Assert.That(sm.State, Is.EqualTo(SessionState.Ready));
    }

    [Test]
    public void Restarting_ShouldGoToStarting()
    {
        var sm = new PluginSessionStateMachine();
        sm.Transition(SessionState.Starting);
        sm.Transition(SessionState.Handshaking);
        sm.Transition(SessionState.Ready);
        sm.Transition(SessionState.Restarting);
        sm.Transition(SessionState.Starting);

        Assert.That(sm.State, Is.EqualTo(SessionState.Starting));
    }

    [Test]
    public void AnyState_CanStopToStoppedViaStopping()
    {
        var sm = new PluginSessionStateMachine();
        sm.Transition(SessionState.Starting);
        sm.Transition(SessionState.Stopping);
        sm.Transition(SessionState.Stopped);

        Assert.That(sm.State, Is.EqualTo(SessionState.Stopped));
    }

    [Test]
    public void Transition_IllegalJump_ShouldThrow()
    {
        var sm = new PluginSessionStateMachine();
        // Cannot go directly from Created to Ready (must pass through Starting/Handshaking).
        Assert.That(() => sm.Transition(SessionState.Ready), Throws.InvalidOperationException);
    }

    [Test]
    public void Transition_FromStopped_ShouldThrow()
    {
        var sm = new PluginSessionStateMachine();
        sm.Transition(SessionState.Starting);
        sm.Transition(SessionState.Stopping);
        sm.Transition(SessionState.Stopped);

        Assert.That(() => sm.Transition(SessionState.Starting), Throws.InvalidOperationException);
    }

    [Test]
    public void IsAvailable_ShouldBeTrueOnlyInReady()
    {
        var sm = new PluginSessionStateMachine();
        Assert.That(sm.IsAvailable, Is.False);

        sm.Transition(SessionState.Starting);
        Assert.That(sm.IsAvailable, Is.False);

        sm.Transition(SessionState.Handshaking);
        Assert.That(sm.IsAvailable, Is.False);

        sm.Transition(SessionState.Ready);
        Assert.That(sm.IsAvailable, Is.True);

        sm.Transition(SessionState.Stopping);
        Assert.That(sm.IsAvailable, Is.False);
    }
}
