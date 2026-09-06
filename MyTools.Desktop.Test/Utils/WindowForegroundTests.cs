using MyTools.Desktop.Utils;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Utils;

[TestFixture]
public class WindowForegroundTests
{
    [Test]
    public void TryActivateHandle_VerifiesForegroundWithoutFlashingOnSuccess()
    {
        var target = new IntPtr(42);
        var foreground = IntPtr.Zero;
        var flashed = false;

        var activated = WindowForeground.TryActivateHandle(
            target,
            handle =>
            {
                foreground = handle;
                return true;
            },
            () => foreground,
            _ => flashed = true);

        Assert.Multiple(() =>
        {
            Assert.That(activated, Is.True);
            Assert.That(flashed, Is.False);
        });
    }

    [Test]
    public void TryActivateHandle_FlashesTaskbarWhenWindowsDeniesForeground()
    {
        var target = new IntPtr(42);
        var flashedHandle = IntPtr.Zero;

        var activated = WindowForeground.TryActivateHandle(
            target,
            _ => false,
            () => new IntPtr(7),
            handle => flashedHandle = handle);

        Assert.Multiple(() =>
        {
            Assert.That(activated, Is.False);
            Assert.That(flashedHandle, Is.EqualTo(target));
        });
    }
}
