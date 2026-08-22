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

    [Test]
    public void Initialize_ShouldOpenDatabaseWithoutLegacyPinnedColumn()
    {
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE clipboard_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    content BLOB NOT NULL,
    summary TEXT NOT NULL,
    hash TEXT NOT NULL,
    timestamp TEXT NOT NULL,
    kind TEXT NOT NULL DEFAULT 'text',
    pixel_width INTEGER NOT NULL DEFAULT 0,
    pixel_height INTEGER NOT NULL DEFAULT 0,
    byte_size INTEGER NOT NULL DEFAULT 0
);";
            command.ExecuteNonQuery();
        }

        var helper = new ClipBoardDbHelper(dbPath);
        helper.AddHistory([1], "migrated", "hash-migrated");

        Assert.That(helper.Search(null, includeContent: false).Single().Summary, Is.EqualTo("migrated"));
    }

    [Test]
    public void Initialize_ShouldClearLegacyPinnedState()
    {
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE clipboard_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    content BLOB NOT NULL,
    summary TEXT NOT NULL,
    hash TEXT NOT NULL,
    timestamp TEXT NOT NULL,
    kind TEXT NOT NULL DEFAULT 'text',
    pixel_width INTEGER NOT NULL DEFAULT 0,
    pixel_height INTEGER NOT NULL DEFAULT 0,
    byte_size INTEGER NOT NULL DEFAULT 0,
    is_pinned INTEGER NOT NULL DEFAULT 0
);
INSERT INTO clipboard_history
    (content, summary, hash, timestamp, is_pinned)
VALUES
    (X'01', 'legacy pinned', 'legacy-hash', @timestamp, 1);";
            command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("o"));
            command.ExecuteNonQuery();
        }

        _ = new ClipBoardDbHelper(dbPath);

        using var verification = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        verification.Open();
        var verifyCommand = verification.CreateCommand();
        verifyCommand.CommandText = "SELECT is_pinned FROM clipboard_history WHERE summary = 'legacy pinned'";
        Assert.That(Convert.ToInt32(verifyCommand.ExecuteScalar()), Is.Zero);
    }

    [Test]
    public void CleanupOldHistory_ShouldLimitHistoryCount()
    {
        var helper = new ClipBoardDbHelper(dbPath);
        for (var index = 0; index < 4; index++)
        {
            helper.AddHistory([(byte)index], $"item-{index}", $"hash-{index}");
        }
        helper.CleanupOldHistory(maxHistoryDays: 30, maxHistoryCount: 2);

        var remaining = helper.Search(null, includeContent: false);
        Assert.That(remaining.Select(item => item.Summary), Is.EqualTo(new[] { "item-3", "item-2" }));
    }

    [Test]
    public void CleanupOldHistory_ShouldDeleteExpiredItems()
    {
        var helper = new ClipBoardDbHelper(dbPath);
        helper.AddHistory([1], "expired", "hash-expired");

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE clipboard_history SET timestamp = @timestamp";
            command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.AddDays(-10).ToString("o"));
            command.ExecuteNonQuery();
        }

        helper.CleanupOldHistory(maxHistoryDays: 3, maxHistoryCount: 50);

        var remaining = helper.Search(null, includeContent: false);
        Assert.That(remaining, Is.Empty);
    }

    [Test]
    public void DeleteHistory_ShouldRemoveOnlyRequestedItem()
    {
        var helper = new ClipBoardDbHelper(dbPath);
        helper.AddHistory([1], "old", "hash-old");
        helper.AddHistory([2], "new", "hash-new");
        var latest = helper.GetLatestHistory();

        Assert.That(latest?.Summary, Is.EqualTo("new"));
        Assert.That(helper.DeleteHistory(latest!.Id), Is.True);
        Assert.That(helper.Search(null, includeContent: false).Select(item => item.Summary),
            Is.EqualTo(new[] { "old" }));
    }
}
