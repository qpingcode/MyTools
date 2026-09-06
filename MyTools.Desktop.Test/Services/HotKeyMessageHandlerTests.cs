using System.Reflection;
using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class HotKeyMessageHandlerTests
{
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
