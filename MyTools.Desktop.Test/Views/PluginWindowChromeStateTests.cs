using System.Windows;
using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class PluginWindowChromeStateTests
{
    [TestCase(true, 0)]
    [TestCase(false, 8)]
    public void PluginContentMargin_WhenRestored_PreservesWebViewResizeHitAreas(
        bool isStatusBarVisible,
        double expectedBottom)
    {
        var margin = PluginWindowLayoutMetrics.GetPluginContentMargin(
            WindowState.Normal,
            isStatusBarVisible);

        Assert.That(margin, Is.EqualTo(new Thickness(8, 0, 8, expectedBottom)));
    }

    [Test]
    public void PluginContentMargin_WhenMaximized_FillsWindow()
    {
        var margin = PluginWindowLayoutMetrics.GetPluginContentMargin(
            WindowState.Maximized,
            isStatusBarVisible: false);

        Assert.That(margin, Is.EqualTo(new Thickness(0)));
    }

    [Test]
    public void From_WhenWindowStateIsMaximized_UsesMaximizedChrome()
    {
        var state = PluginWindowChromeState.From(WindowState.Maximized);

        Assert.Multiple(() =>
        {
            Assert.That(state.CornerRadius, Is.EqualTo(new CornerRadius(0)));
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
            Assert.That(state.CornerRadius, Is.EqualTo(new CornerRadius(12)));
            Assert.That(state.ShowRestoreIcon, Is.False);
            Assert.That(state.CloseButtonCornerRadius, Is.EqualTo(new CornerRadius(0, 12, 0, 0)));
        });
    }
}
