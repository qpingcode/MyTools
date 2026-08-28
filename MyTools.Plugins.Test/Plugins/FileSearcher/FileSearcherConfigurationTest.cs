using System.Text.Json;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins.FileSearcher;

[TestFixture]
public class FileSearcherConfigurationTest
{
    [Test]
    public void ReadSearchDirectories_NormalizesAndDeduplicatesPaths()
    {
        var first = Path.Combine(Path.GetTempPath(), "FileSearcher-A");
        var second = Path.Combine(Path.GetTempPath(), "FileSearcher-B");
        var value = JsonSerializer.SerializeToElement(new object[]
        {
            new { Path = first + Path.DirectorySeparatorChar },
            new { path = first.ToUpperInvariant() },
            new { Path = second },
            new { Path = "" }
        });

        var result = MyTools.Plugins.FileSearcher.ReadSearchDirectories(value);

        Assert.That(result, Is.EquivalentTo(new[] { Path.GetFullPath(first), Path.GetFullPath(second) }));
    }

    [Test]
    public void CalculateDirectoryChanges_ReturnsOnlyAddedAndRemovedDirectories()
    {
        var changes = MyTools.Plugins.FileSearcher.CalculateDirectoryChanges(
            [@"C:\\keep", @"C:\\remove"],
            [@"c:\\KEEP", @"C:\\add"]);

        Assert.Multiple(() =>
        {
            Assert.That(changes.Added, Is.EqualTo(new[] { @"C:\\add" }));
            Assert.That(changes.Removed, Is.EqualTo(new[] { @"C:\\remove" }));
        });
    }
}
