using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class HotKeyInspectorTests
{
    [Test]
    public void Inspect_ShouldFlagReservedCopyShortcut()
    {
        var result = HotKeyInspector.Inspect("Ctrl+C", new HotKeyInspectionRequest());

        Assert.That(result.Reserved, Is.True);
        Assert.That(result.ConflictWith, Is.Null);
    }

    [Test]
    public void Inspect_ShouldDetectSearchHotKeyConflict()
    {
        var result = HotKeyInspector.Inspect("Alt+Space", new HotKeyInspectionRequest
        {
            SearchHotKey = "Alt+Space",
            SearchHotKeyDisplayName = "Search hotkey"
        });

        Assert.That(result.ConflictWith, Is.EqualTo("Search hotkey"));
        Assert.That(result.Reserved, Is.False);
    }

    [Test]
    public void Inspect_ShouldIgnoreSearchHotKeyWhenExcluded()
    {
        var result = HotKeyInspector.Inspect("Alt+Space", new HotKeyInspectionRequest
        {
            SearchHotKey = "Alt+Space",
            ExcludeSearchHotKey = true
        });

        Assert.That(result.ConflictWith, Is.Null);
    }

    [Test]
    public void Inspect_ShouldDetectPluginHotKeyConflict()
    {
        var result = HotKeyInspector.Inspect("Alt+S", new HotKeyInspectionRequest
        {
            ExcludePluginId = "formatter",
            PluginHotKeys = new Dictionary<string, string?>
            {
                ["settings"] = "Alt+S",
                ["formatter"] = "Alt+S"
            },
            PluginNames = new Dictionary<string, string>
            {
                ["settings"] = "Settings"
            }
        });

        Assert.That(result.ConflictWith, Is.EqualTo("Settings"));
    }

    [Test]
    public void Inspect_ShouldIgnoreReservedHotKeyWhenExcluded()
    {
        var result = HotKeyInspector.Inspect("Ctrl+C", new HotKeyInspectionRequest
        {
            ExcludeReservedHotKey = true
        });

        Assert.That(result.Reserved, Is.False);
        Assert.That(result.ConflictWith, Is.Null);
    }

    [Test]
    public void Inspect_EmptyHotKey_ShouldBeClean()
    {
        var result = HotKeyInspector.Inspect("", new HotKeyInspectionRequest
        {
            SearchHotKey = "Alt+Space"
        });

        Assert.That(result.ConflictWith, Is.Null);
        Assert.That(result.Reserved, Is.False);
    }
}
