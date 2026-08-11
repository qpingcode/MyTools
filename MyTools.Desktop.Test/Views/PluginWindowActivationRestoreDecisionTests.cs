using System.Windows;
using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class PluginWindowActivationRestoreDecisionTests
{
    [Test]
    public void From_WhenWindowIsMinimized_UsesNativeRestore()
    {
        var action = PluginWindowActivationRestoreDecision.From(WindowState.Minimized);

        Assert.That(action, Is.EqualTo(PluginWindowActivationRestoreAction.NativeRestore));
    }

    [TestCase(WindowState.Normal)]
    [TestCase(WindowState.Maximized)]
    public void From_WhenWindowIsNotMinimized_DoesNotUseNativeRestore(WindowState windowState)
    {
        var action = PluginWindowActivationRestoreDecision.From(windowState);

        Assert.That(action, Is.EqualTo(PluginWindowActivationRestoreAction.None));
    }
}
