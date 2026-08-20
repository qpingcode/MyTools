using System.Windows;
using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class PluginWindowChromeStateTests
{
    [Test]
    public void From_WhenWindowStateIsMaximized_UsesMaximizedChrome()
    {
        var state = PluginWindowChromeState.From(WindowState.Maximized);

        Assert.Multiple(() =>
        {
            Assert.That(state.FrameMargin, Is.EqualTo(new Thickness(0)));
            Assert.That(state.CornerRadius, Is.EqualTo(new CornerRadius(0)));
            Assert.That(state.ShowShadow, Is.False);
            Assert.That(state.ShowRestoreIcon, Is.True);
            Assert.That(state.CloseButtonCornerRadius, Is.EqualTo(new CornerRadius(0)));
        });
    }

    [TestCase(WindowState.Normal)]
    [TestCase(WindowState.Minimized)]
    public void From_WhenWindowStateIsNormalOrMinimized_UsesRestoredChrome(WindowState windowState)
    {
        var state = PluginWindowChromeState.From(windowState);

        Assert.Multiple(() =>
        {
            Assert.That(state.FrameMargin, Is.EqualTo(new Thickness(10)));
            Assert.That(state.CornerRadius, Is.EqualTo(new CornerRadius(12)));
            Assert.That(state.ShowShadow, Is.True);
            Assert.That(state.ShowRestoreIcon, Is.False);
            Assert.That(state.CloseButtonCornerRadius, Is.EqualTo(new CornerRadius(0, 12, 0, 0)));
        });
    }
}
