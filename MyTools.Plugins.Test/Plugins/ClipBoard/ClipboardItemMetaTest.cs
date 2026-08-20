using System.Globalization;
using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins.ClipBoard;

[TestFixture]
public class ClipboardItemMetaTest
{
    [Test]
    public void FormatDimensionSubtitle_WhenValid_UsesBracketTimesForm()
    {
        Assert.That(ClipboardItemMeta.FormatDimensionSubtitle(800, 600), Is.EqualTo("[800×600]"));
    }

    [Test]
    public void FormatDimensionSubtitle_WhenMissing_IsEmpty()
    {
        Assert.That(ClipboardItemMeta.FormatDimensionSubtitle(0, 600), Is.EqualTo(string.Empty));
    }

    [Test]
    public void FormatByteSize_UsesCompactUnits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClipboardItemMeta.FormatByteSize(512), Is.EqualTo("512 B"));
            Assert.That(ClipboardItemMeta.FormatByteSize(1536), Is.EqualTo("1.5 KB"));
            Assert.That(ClipboardItemMeta.FormatByteSize(2 * 1024 * 1024), Is.EqualTo("2 MB"));
        });
    }

    [Test]
    public void FormatListTitle_WhenImageWithDimensions_UsesImageAndTimesSign()
    {
        Assert.That(ClipboardItemMeta.FormatListTitle("[Image]", 778, 211), Is.EqualTo("Image [778×211]"));
        Assert.That(ClipboardItemMeta.FormatListTitle("Image", 778, 211), Is.EqualTo("Image [778×211]"));
    }

    [Test]
    public void FormatListTitle_WhenImageWithoutDimensions_UsesImage()
    {
        Assert.That(ClipboardItemMeta.FormatListTitle("[Image]", 0, 0), Is.EqualTo("Image"));
    }

    [Test]
    public void FormatListTitle_WhenPlainTextWithoutDimensions_KeepsSummary()
    {
        Assert.That(ClipboardItemMeta.FormatListTitle("copied text", 0, 0), Is.EqualTo("copied text"));
    }

    [Test]
    public void FormatListTitle_WhenPlainTextWithDimensions_AppendsSize()
    {
        Assert.That(ClipboardItemMeta.FormatListTitle("screenshot", 800, 600), Is.EqualTo("screenshot [800×600]"));
    }

    [Test]
    public void TryParsePreviewDimensions_FromTitle_UsesSpacedTimesSign()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClipboardItemMeta.TryParsePreviewDimensions("Image [778×211]", out var fromTimes), Is.True);
            Assert.That(fromTimes, Is.EqualTo("778 × 211"));

            Assert.That(ClipboardItemMeta.TryParsePreviewDimensions("Image [778*211]", out var fromAsterisk), Is.True);
            Assert.That(fromAsterisk, Is.EqualTo("778 × 211"));

            Assert.That(ClipboardItemMeta.TryParsePreviewDimensions("Image [778 × 211]", out var fromSpaced), Is.True);
            Assert.That(fromSpaced, Is.EqualTo("778 × 211"));

            Assert.That(ClipboardItemMeta.TryParsePreviewDimensions("copied text", out var missing), Is.False);
            Assert.That(missing, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void FormatLocalTimestamp_UsesCultureShortDateTime()
    {
        var local = new DateTime(2026, 8, 21, 12, 30, 0, DateTimeKind.Local);
        var zh = new CultureInfo("zh-CN");
        var en = new CultureInfo("en-US");
        var fr = new CultureInfo("fr-FR");

        Assert.Multiple(() =>
        {
            Assert.That(ClipboardItemMeta.FormatLocalTimestamp(null, zh), Is.EqualTo(string.Empty));
            Assert.That(ClipboardItemMeta.FormatLocalTimestamp(default(DateTime), zh), Is.EqualTo(string.Empty));
            Assert.That(ClipboardItemMeta.FormatLocalTimestamp(local, zh), Is.EqualTo(local.ToString("g", zh)));
            Assert.That(ClipboardItemMeta.FormatLocalTimestamp(local, en), Is.EqualTo(local.ToString("g", en)));
            Assert.That(ClipboardItemMeta.FormatLocalTimestamp(local, fr), Is.EqualTo(local.ToString("g", fr)));
            Assert.That(ClipboardItemMeta.FormatLocalTimestamp(local, zh), Is.Not.EqualTo(ClipboardItemMeta.FormatLocalTimestamp(local, en)));
        });
    }
}
