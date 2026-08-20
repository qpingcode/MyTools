using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins.ClipBoard;

[TestFixture]
public class ClipBoardDbHelperKindTest
{
    private string tempDirectory = null!;
    private string dbPath = null!;

    [SetUp]
    public void SetUp()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        dbPath = Path.Combine(tempDirectory, "clipboard_history.db");
    }

    [TearDown]
    public void TearDown()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Test]
    public void Search_ReturnsStoredKind()
    {
        var helper = new ClipBoardDbHelper(dbPath);
        helper.AddHistory([1, 2, 3], "photo", "hash-image", ClipboardContentKind.Image);

        var item = helper.Search(null, includeContent: false).Single();

        Assert.That(item.Kind, Is.EqualTo(ClipboardContentKind.Image));
    }

    [Test]
    public void Search_ReturnsStoredImageDimensions()
    {
        var helper = new ClipBoardDbHelper(dbPath);
        helper.AddHistory([1, 2, 3], "[Image]", "hash-image-size", ClipboardContentKind.Image, 800, 600, 2048);

        var item = helper.Search(null, includeContent: false).Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.PixelWidth, Is.EqualTo(800));
            Assert.That(item.PixelHeight, Is.EqualTo(600));
            Assert.That(item.ByteSize, Is.EqualTo(2048));
            Assert.That(ClipboardItemMeta.FormatDimensionSubtitle(item.PixelWidth, item.PixelHeight), Is.EqualTo("[800×600]"));
        });
    }
}
