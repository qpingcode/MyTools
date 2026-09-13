using MyTools.Desktop.Services;
using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class PluginDockLayoutMetricsTests
{
    private static readonly DipRect PrimaryWorkArea = new(0, 0, 1920, 1040);

    [Test]
    public void DefaultPlacement_AnchorsToBottomLeftWorkAreaMargin()
    {
        const double width = 56;
        const double height = 80;

        var placement = PluginDockLayoutMetrics.DefaultPlacement(PrimaryWorkArea, width, height);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Left, Is.EqualTo(PluginDockLayoutMetrics.WorkAreaMargin));
            Assert.That(placement.Top, Is.EqualTo(
                PrimaryWorkArea.Bottom - PluginDockLayoutMetrics.WorkAreaMargin - height));
            Assert.That(placement.Width, Is.EqualTo(width));
            Assert.That(placement.Height, Is.EqualTo(height));
        });
    }

    [Test]
    public void DefaultPlacement_WhenStackIsTallerThanWorkArea_ClampsInsideWorkArea()
    {
        var placement = PluginDockLayoutMetrics.DefaultPlacement(PrimaryWorkArea, 56, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Left, Is.EqualTo(PluginDockLayoutMetrics.WorkAreaMargin));
            Assert.That(placement.Top, Is.EqualTo(0));
            Assert.That(placement.Height, Is.EqualTo(PrimaryWorkArea.Height));
        });
    }
}
