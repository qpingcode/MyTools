using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class PluginWindowMaximizedBoundsTests
{
    [Test]
    public void FromMonitorInfo_WhenWindowIsOnPrimaryStyleMonitor_UsesWorkAreaSize()
    {
        var bounds = PluginWindowMaximizedBounds.FromMonitorInfo(
            new PluginWindowNativeRect(0, 0, 1920, 1080),
            new PluginWindowNativeRect(0, 0, 1920, 1040));

        Assert.Multiple(() =>
        {
            Assert.That(bounds.PositionX, Is.Zero);
            Assert.That(bounds.PositionY, Is.Zero);
            Assert.That(bounds.Width, Is.EqualTo(1920));
            Assert.That(bounds.Height, Is.EqualTo(1040));
        });
    }

    [Test]
    public void FromMonitorInfo_WhenWindowIsOnNegativeCoordinateMonitor_UsesMonitorRelativeOffsets()
    {
        var bounds = PluginWindowMaximizedBounds.FromMonitorInfo(
            new PluginWindowNativeRect(-1600, -200, -200, 900),
            new PluginWindowNativeRect(-1580, -160, -200, 860));

        Assert.Multiple(() =>
        {
            Assert.That(bounds.PositionX, Is.EqualTo(20));
            Assert.That(bounds.PositionY, Is.EqualTo(40));
            Assert.That(bounds.Width, Is.EqualTo(1380));
            Assert.That(bounds.Height, Is.EqualTo(1020));
        });
    }
}
