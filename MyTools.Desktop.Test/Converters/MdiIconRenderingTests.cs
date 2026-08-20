using System.Windows;
using System.Windows.Controls;
using MyTools.Desktop.Converters;
using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Converters;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class MdiIconRenderingTests
{
    [Test]
    public void GlyphLookup_ResolvesFormatTextFromMdiMap()
    {
        var glyph = MdiGlyphLookup.Get("mdi-format-text");

        Assert.That(glyph, Is.EqualTo(char.ConvertFromUtf32(0xF0284)));
    }

    [Test]
    public void IconTemplateSelector_SelectsMdiTemplate()
    {
        var mdiTemplate = new DataTemplate();
        var selector = new IconTemplateSelector
        {
            StringIconTemplate = new DataTemplate(),
            ImageIconTemplate = new DataTemplate(),
            MdiIconTemplate = mdiTemplate
        };

        var selected = selector.SelectTemplate(new MdiIcon("mdi-file-outline"), new ContentControl());

        Assert.That(selected, Is.SameAs(mdiTemplate));
    }
}
