using MyTools.Common.Theming;
using NUnit.Framework;

namespace MyTools.Common.Test;

public class ThemeKindExtensionsTest
{
    [Test]
    public void ToWireString_ReturnsLowercaseWireValue()
    {
        Assert.That(ThemeKind.Light.ToWireString(), Is.EqualTo("light"));
        Assert.That(ThemeKind.Dark.ToWireString(), Is.EqualTo("dark"));
    }

    [Test]
    public void Parse_IsCaseInsensitive()
    {
        Assert.That(ThemeKindExtensions.Parse("light"), Is.EqualTo(ThemeKind.Light));
        Assert.That(ThemeKindExtensions.Parse("LIGHT"), Is.EqualTo(ThemeKind.Light));
        Assert.That(ThemeKindExtensions.Parse("dark"), Is.EqualTo(ThemeKind.Dark));
    }

    [Test]
    public void Parse_RoundTripsToWireString()
    {
        foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            Assert.That(ThemeKindExtensions.Parse(kind.ToWireString()), Is.EqualTo(kind));
        }
    }

    [Test]
    public void Parse_UnknownValueFallsBackToDark()
    {
        Assert.That(ThemeKindExtensions.Parse("purple"), Is.EqualTo(ThemeKind.Dark));
        Assert.That(ThemeKindExtensions.Parse(""), Is.EqualTo(ThemeKind.Dark));
        Assert.That(ThemeKindExtensions.Parse(null), Is.EqualTo(ThemeKind.Dark));
        Assert.That(ThemeKindExtensions.Parse("   "), Is.EqualTo(ThemeKind.Dark));
    }
}
