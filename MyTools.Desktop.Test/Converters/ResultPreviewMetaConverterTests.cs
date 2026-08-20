using System.Globalization;
using MyTools.Common;
using MyTools.Desktop.Converters;
using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Converters;

[TestFixture]
public class ResultPreviewMetaConverterTests
{
    [Test]
    public void Convert_WhenNotResultItem_ReturnsEmpty()
    {
        var converter = new ResultPreviewMetaConverter();

        Assert.That(
            converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo(string.Empty));
    }

    [Test]
    public void TitleDimensions_AreParsedForPreview()
    {
        Assert.That(ClipboardItemMeta.TryParsePreviewDimensions("Image [778×211]", out var dimensions), Is.True);
        Assert.That(dimensions, Is.EqualTo("778 × 211"));
    }
}
