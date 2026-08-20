using System.Windows;
using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class WindowFocusTopmostTests
{
    [Test]
    public void OnActivated_SetsTopmostTrue()
    {
        var window = new Window { Topmost = false };

        WindowFocusTopmost.OnActivated(window, EventArgs.Empty);

        Assert.That(window.Topmost, Is.True);
    }

    [Test]
    public void OnDeactivated_SetsTopmostFalse()
    {
        var window = new Window { Topmost = true };

        WindowFocusTopmost.OnDeactivated(window, EventArgs.Empty);

        Assert.That(window.Topmost, Is.False);
    }

    [Test]
    public void Attach_TogglesTopmostWithActivationEvents()
    {
        var window = new Window { Topmost = false };
        WindowFocusTopmost.Attach(window);

        Invoke(window, "OnActivated");
        Assert.That(window.Topmost, Is.True);

        Invoke(window, "OnDeactivated");
        Assert.That(window.Topmost, Is.False);
    }

    private static void Invoke(Window window, string methodName)
    {
        var method = typeof(Window).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method!.Invoke(window, [EventArgs.Empty]);
    }
}
