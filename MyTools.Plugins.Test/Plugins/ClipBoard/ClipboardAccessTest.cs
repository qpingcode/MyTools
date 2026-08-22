using System.Runtime.InteropServices;
using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins.ClipBoard;

[TestFixture]
public class ClipboardAccessTest
{
    [Test]
    public void Execute_ShouldRetryClipboardBusyFailures()
    {
        var attempts = 0;

        var result = ClipboardAccess.Execute(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new COMException("busy", unchecked((int)0x800401D0));
            }

            return "copied";
        }, [0, 0]);

        Assert.That(result, Is.EqualTo("copied"));
        Assert.That(attempts, Is.EqualTo(3));
    }

    [Test]
    public void Execute_ShouldRethrowAfterRetriesAreExhausted()
    {
        var attempts = 0;

        Assert.That(
            () => ClipboardAccess.Execute<string>(() =>
            {
                attempts++;
                throw new COMException("busy", unchecked((int)0x800401D0));
            }, [0, 0]),
            Throws.TypeOf<COMException>());
        Assert.That(attempts, Is.EqualTo(3));
    }

    [Test]
    public void Execute_ShouldNotRetryOtherComFailures()
    {
        var attempts = 0;

        Assert.That(
            () => ClipboardAccess.Execute<string>(() =>
            {
                attempts++;
                throw new COMException("other", unchecked((int)0x80004005));
            }, [0, 0]),
            Throws.TypeOf<COMException>());
        Assert.That(attempts, Is.EqualTo(1));
    }
}
