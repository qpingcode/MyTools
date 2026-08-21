using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class WindowPlacementFitTest
{
    private static readonly DipRect Primary = new(0, 0, 1920, 1040);
    private static readonly DipRect Secondary = new(1920, 0, 1600, 900);
    private static readonly DipRect LeftNegative = new(-1600, -200, 1400, 900);

    [Test]
    public void FromRelative_PlacesOnTargetWorkArea()
    {
        var relative = new DipRect(80, 40, 800, 600);

        var fitted = WindowPlacementFit.FromRelative(relative, Secondary);

        Assert.That(fitted, Is.EqualTo(new DipRect(2000, 40, 800, 600)));
    }

    [Test]
    public void FromRelative_WhenOffsetExceedsWorkArea_ClampsToWorkArea()
    {
        var relative = new DipRect(2000, 80, 800, 600);

        var fitted = WindowPlacementFit.FromRelative(relative, Primary);

        Assert.That(fitted.Left, Is.EqualTo(1920 - 800));
        Assert.That(fitted.Top, Is.EqualTo(80));
        Assert.That(fitted.Width, Is.EqualTo(800));
        Assert.That(fitted.Height, Is.EqualTo(600));
    }

    [Test]
    public void FromRelative_WhenWidthExceedsWorkArea_ShrinksToFit()
    {
        var relative = new DipRect(100, 80, 3000, 600);

        var fitted = WindowPlacementFit.FromRelative(relative, Primary);

        Assert.That(fitted.Width, Is.EqualTo(1920));
        Assert.That(fitted.Height, Is.EqualTo(600));
        Assert.That(fitted.Left, Is.EqualTo(0));
        Assert.That(fitted.Top, Is.EqualTo(80));
    }

    [Test]
    public void ToRelative_SubtractsWorkAreaOrigin()
    {
        var absolute = new DipRect(-1500, -100, 700, 500);

        var relative = WindowPlacementFit.ToRelative(absolute, LeftNegative);

        Assert.That(relative, Is.EqualTo(new DipRect(100, 100, 700, 500)));
    }

    [Test]
    public void CenterOn_CentersWithinWorkArea()
    {
        var centered = WindowPlacementFit.CenterOn(Secondary, 800, 600);

        Assert.That(centered.Left, Is.EqualTo(1920 + 400));
        Assert.That(centered.Top, Is.EqualTo(150));
        Assert.That(centered.Width, Is.EqualTo(800));
        Assert.That(centered.Height, Is.EqualTo(600));
    }

    [Test]
    public void CenterOn_WhenLargerThanWorkArea_FillsWorkArea()
    {
        var centered = WindowPlacementFit.CenterOn(Primary, 3000, 2000);

        Assert.That(centered, Is.EqualTo(new DipRect(0, 0, 1920, 1040)));
    }
}
