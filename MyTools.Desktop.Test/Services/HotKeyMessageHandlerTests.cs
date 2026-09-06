using System.Reflection;
using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class HotKeyMessageHandlerTests
{
    private static readonly FieldInfo ForegroundCallbacksField = typeof(HotKeyMessageHandler)
        .GetField("_foregroundCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo SynchronousCallbacksField = typeof(HotKeyMessageHandler)
        .GetField("_synchronousCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Test]
    public void Handle_SynchronousHotKeyRunsOnceInsideMessageCallback()
    {
        var invocations = 0;
        var scheduled = false;
        var handler = new HotKeyMessageHandler(_ => scheduled = true);
        ((Dictionary<int, Action>)CallbacksField.GetValue(handler)!)[42] = () => invocations++;
        ((HashSet<int>)SynchronousCallbacksField.GetValue(handler)!).Add(42);
        var handled = false;

        handler.Handle(0, new IntPtr(42), IntPtr.Zero, ref handled);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(invocations, Is.EqualTo(1));
            Assert.That(scheduled, Is.False);
        });
    }

    [Test]
    public void Handle_SynchronousHotKeyDefersOnlyWhenInlineCallbackThrows()
    {
        Action? scheduled = null;
        Action callback = () => throw new InvalidOperationException("Inline activation failed");
        var handler = new HotKeyMessageHandler(action => scheduled = action);
        ((Dictionary<int, Action>)CallbacksField.GetValue(handler)!)[42] = callback;
        ((HashSet<int>)SynchronousCallbacksField.GetValue(handler)!).Add(42);
        var handled = false;

        Assert.DoesNotThrow(() => handler.Handle(0, new IntPtr(42), IntPtr.Zero, ref handled));
        Assert.That(handled, Is.True);
        Assert.That(scheduled, Is.SameAs(callback));
    }

    [Test]
    public void Handle_ActivatesShellBeforeReturningButDefersPluginContent()
    {
        var phases = new List<string>();
        Action? scheduled = null;
        var handler = new HotKeyMessageHandler(callback => scheduled = callback);
        ((Dictionary<int, Action>)CallbacksField.GetValue(handler)!)[42] = () => phases.Add("content");
        ((Dictionary<int, Action>)ForegroundCallbacksField.GetValue(handler)!)[42] = () => phases.Add("foreground");
        var handled = false;

        handler.Handle(0, new IntPtr(42), IntPtr.Zero, ref handled);

        Assert.That(handled, Is.True);
        Assert.That(phases, Is.EqualTo(new[] { "foreground" }));
        Assert.That(scheduled, Is.Not.Null);
        scheduled!();
        Assert.That(phases, Is.EqualTo(new[] { "foreground", "content" }));
    }

    [Test]
    public void Handle_ForegroundFailureStillSchedulesNormalOpen()
    {
        Action? scheduled = null;
        var handler = new HotKeyMessageHandler(callback => scheduled = callback);
        Action content = () => { };
        ((Dictionary<int, Action>)CallbacksField.GetValue(handler)!)[42] = content;
        ((Dictionary<int, Action>)ForegroundCallbacksField.GetValue(handler)!)[42] =
            () => throw new InvalidOperationException("Activation failed");
        var handled = false;

        Assert.DoesNotThrow(() => handler.Handle(0, new IntPtr(42), IntPtr.Zero, ref handled));
        Assert.That(handled, Is.True);
        Assert.That(scheduled, Is.SameAs(content));
    }

    private static readonly FieldInfo CallbacksField = typeof(HotKeyMessageHandler)
        .GetField("_callbacks", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not find HotKeyMessageHandler._callbacks.");

    [Test]
    public void Handle_SchedulesCallbackAfterHotKeyMessageReturns()
    {
        Action? scheduled = null;
        var callbackInvoked = false;
        var handler = new HotKeyMessageHandler(callback => scheduled = callback);
        var callbacks = (Dictionary<int, Action>)CallbacksField.GetValue(handler)!;
        callbacks[42] = () => callbackInvoked = true;
        var handled = false;

        handler.Handle(0, new IntPtr(42), IntPtr.Zero, ref handled);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(scheduled, Is.Not.Null);
            Assert.That(callbackInvoked, Is.False);
        });

        scheduled!();
        Assert.That(callbackInvoked, Is.True);
    }
}
