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

    [Test]
    public void Place_OffsetsEachWindowByCascadeStepTimesIndex()
    {
        var first = new DipRect(100, 200, 420, 310);

        var second = PluginDockWindowArrangement.Place(first, 1);
        var third = PluginDockWindowArrangement.Place(first, 2);

        Assert.Multiple(() =>
        {
            Assert.That(PluginDockWindowArrangement.Place(first, 0), Is.EqualTo(first));
            Assert.That(second.Left, Is.EqualTo(first.Left + PluginDockLayoutMetrics.ArrangeCascadeStep));
            Assert.That(second.Top, Is.EqualTo(first.Top + PluginDockLayoutMetrics.ArrangeCascadeStep));
            Assert.That(second.Width, Is.EqualTo(first.Width));
            Assert.That(second.Height, Is.EqualTo(first.Height));
            Assert.That(third.Left, Is.EqualTo(first.Left + PluginDockLayoutMetrics.ArrangeCascadeStep * 2));
            Assert.That(third.Top, Is.EqualTo(first.Top + PluginDockLayoutMetrics.ArrangeCascadeStep * 2));
        });
    }
}
