using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Sessions;
using MyTools.Protocol.Errors;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Bus;

[TestFixture]
public class RequestAdmissionTest
{
    [Test]
    public void Check_WhenSessionReady_ShouldAdmit()
    {
        var sm = new PluginSessionStateMachine();
        sm.Transition(SessionState.Starting);
        sm.Transition(SessionState.Handshaking);
        sm.Transition(SessionState.Ready);

        var result = RequestAdmission.Check(sm);

        Assert.That(result.IsAdmitted, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [TestCase(SessionState.Created)]
    [TestCase(SessionState.Starting)]
    [TestCase(SessionState.Handshaking)]
    [TestCase(SessionState.Restarting)]
    [TestCase(SessionState.Stopping)]
    [TestCase(SessionState.Stopped)]
    public void Check_WhenSessionNotReady_ShouldReturnPluginUnavailable(SessionState state)
    {
        // Build a state machine into the given state through a legal path where possible.
        var sm = new PluginSessionStateMachine();
        switch (state)
        {
            case SessionState.Created:
                break;
            case SessionState.Starting:
                sm.Transition(SessionState.Starting);
                break;
            case SessionState.Handshaking:
                sm.Transition(SessionState.Starting);
                sm.Transition(SessionState.Handshaking);
                break;
            case SessionState.Ready:
                sm.Transition(SessionState.Starting);
                sm.Transition(SessionState.Handshaking);
                sm.Transition(SessionState.Ready);
                break;
            case SessionState.Restarting:
                sm.Transition(SessionState.Starting);
                sm.Transition(SessionState.Handshaking);
                sm.Transition(SessionState.Ready);
                sm.Transition(SessionState.Restarting);
                break;
            case SessionState.Stopping:
                sm.Transition(SessionState.Starting);
                sm.Transition(SessionState.Stopping);
                break;
            case SessionState.Stopped:
                sm.Transition(SessionState.Starting);
                sm.Transition(SessionState.Stopping);
                sm.Transition(SessionState.Stopped);
                break;
        }

        var result = RequestAdmission.Check(sm);

        Assert.That(result.IsAdmitted, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.PluginUnavailable));
    }

    [Test]
    public void Timeout_ForExceededDuration_ShouldReturnRequestTimeout()
    {
        var result = RequestAdmission.TimeoutError(allowedMs: 5000, elapsedMs: 6000);

        Assert.That(result.Code, Is.EqualTo(ErrorCode.RequestTimeout));
        Assert.That(result.Retryable, Is.False);
    }
}
