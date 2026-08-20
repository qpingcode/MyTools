using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins.ClipBoard;

[TestFixture]
public class ClipboardContentKindClassifierTest
{
    [Test]
    public void FileDrop_WinsOverTextAndImage()
    {
        var kind = ClipboardContentKindClassifier.Classify(
            [ClipboardContentKindClassifier.FileDrop, ClipboardContentKindClassifier.UnicodeText, ClipboardContentKindClassifier.Bitmap],
            @"C:\temp\a.png");

        Assert.That(kind, Is.EqualTo(ClipboardContentKind.File));
    }

    [Test]
    public void ImageWithCaption_IsMixed()
    {
        var kind = ClipboardContentKindClassifier.Classify(
            [ClipboardContentKindClassifier.Bitmap, ClipboardContentKindClassifier.UnicodeText],
            "screenshot caption");

        Assert.That(kind, Is.EqualTo(ClipboardContentKind.Mixed));
    }

    [Test]
    public void ImageWithoutText_IsImage()
    {
        var kind = ClipboardContentKindClassifier.Classify(
            [ClipboardContentKindClassifier.Bitmap],
            "");

        Assert.That(kind, Is.EqualTo(ClipboardContentKind.Image));
    }

    [Test]
    public void PlainText_IsText()
    {
        var kind = ClipboardContentKindClassifier.Classify(
            [ClipboardContentKindClassifier.UnicodeText],
            "hello");

        Assert.That(kind, Is.EqualTo(ClipboardContentKind.Text));
    }

    [Test]
    public void HtmlAndRtfWithoutBitmap_AreText()
    {
        var kind = ClipboardContentKindClassifier.Classify(
            [ClipboardContentKindClassifier.Html, ClipboardContentKindClassifier.Rtf],
            "");

        Assert.That(kind, Is.EqualTo(ClipboardContentKind.Text));
    }

    [Test]
    public void UnknownFormats_AreOther()
    {
        var kind = ClipboardContentKindClassifier.Classify(["CustomFormat"], "");

        Assert.That(kind, Is.EqualTo(ClipboardContentKind.Other));
    }

    [Test]
    public void ForClipboardKind_UsesSettingsMdiNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MdiIcon.ForClipboardKind(ClipboardContentKind.Text).Name, Is.EqualTo("mdi-format-text"));
            Assert.That(MdiIcon.ForClipboardKind(ClipboardContentKind.Image).Name, Is.EqualTo("mdi-image-outline"));
            Assert.That(MdiIcon.ForClipboardKind(ClipboardContentKind.File).Name, Is.EqualTo("mdi-file-outline"));
            Assert.That(MdiIcon.ForClipboardKind(ClipboardContentKind.Mixed).Name, Is.EqualTo("mdi-puzzle-outline"));
            Assert.That(MdiIcon.ForClipboardKind(ClipboardContentKind.Other).Name, Is.EqualTo("mdi-help-circle-outline"));
        });
    }
}
