using System.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Common;
using MyTools.Plugins.Param;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins.ClipBoard;

[TestFixture]
public class ClipBoardActionsTest
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
    public void Actions_ShouldExposeRequestedHotkeys()
    {
        var plugin = new ClipBoardPlugin(NullLogger<ClipBoardPlugin>.Instance);

        var hotkeys = plugin.Actions.Select(action => action.Hotkey).ToList();

        Assert.That(hotkeys, Is.EquivalentTo(new[]
        {
            Hotkey.Enter,
            Hotkey.Ctrl(HotkeyKey.Enter),
            Hotkey.Ctrl(HotkeyKey.E)
        }));
    }

    [Test]
    public void LazyClipboardParam_ShouldReturnUnicodePlainText()
    {
        var helper = new ClipBoardDbHelper(dbPath);
        var dataObject = new DataObject();
        dataObject.SetText("plain text", TextDataFormat.UnicodeText);
        helper.AddHistory(DataObjectSerializer.SerializeIDataObject(dataObject), "plain text", "hash");
        var item = helper.Search(null, includeContent: false).Single();

        var text = new LazyClipboardParam(helper, item.Id).GetPlainText();

        Assert.That(text, Is.EqualTo("plain text"));
    }
}
