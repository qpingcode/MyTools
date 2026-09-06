using System.Text.Json;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RamDirectory = Lucene.Net.Store.RAMDirectory;

namespace MyTools.Plugins.Test.Plugins.FileSearcher;

[TestFixture]
public class FileSearcherConfigurationTest
{
    private const LuceneVersion TestLuceneVersion = LuceneVersion.LUCENE_48;

    [Test]
    public void IndexStoragePaths_BelongToFileSearcherPluginDataDirectory()
    {
        var pluginsDataRoot = Path.Combine(Path.GetTempPath(), "pluginsData");
        var pluginDataDirectory = Path.Combine(pluginsDataRoot, "file-searcher");

        Assert.Multiple(() =>
        {
            Assert.That(
                MyTools.Plugins.FileSearcher.GetIndexDirectory(pluginsDataRoot),
                Is.EqualTo(Path.Combine(pluginDataDirectory, "FileSearcherIndex")));
            Assert.That(
                MyTools.Plugins.FileSearcher.GetIndexedRootsPath(pluginsDataRoot),
                Is.EqualTo(Path.Combine(pluginDataDirectory, "FileSearcherIndexedRoots.json")));
        });
    }

    [Test]
    public void DefaultSearchDirectories_UseProfileAndProgramData()
    {
        var result = MyTools.Plugins.FileSearcher.ReadSearchDirectories(
            MyTools.Plugins.FileSearcher.CreateDefaultSearchDirectories());

        Assert.That(result, Is.EquivalentTo(new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        }));
    }

    [Test]
    public void DefaultIgnorePatterns_AreReturnedAsAList()
    {
        var result = MyTools.Plugins.FileSearcher.ReadIgnorePatterns(
            MyTools.Plugins.FileSearcher.CreateDefaultIgnorePatterns());

        Assert.That(result, Is.EqualTo(new[]
        {
            "*.tmp",
            "*.temp",
            "node_modules",
            "**/tmp/**",
            "**/temp/**",
            "**/[Cc]ache/**",
            "**/[Cc]aches/**",
            "**/AppData/**"
        }));
    }

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

    [TestCase(@"C:\Users\example\notes.txt", false)]
    [TestCase(@"C:\Users\example\.gitignore", true)]
    [TestCase(@"C:\Users\example\project\.IGNORE", true)]
    public void IsIgnoreRulesFile_OnlyMatchesIgnoreFiles(string path, bool expected)
    {
        Assert.That(MyTools.Plugins.FileSearcher.IsIgnoreRulesFile(path), Is.EqualTo(expected));
    }

    [Test]
    public void SearchQuery_MatchesMultipleDirectoryPathTerms()
    {
        using var directory = new RamDirectory();
        using var analyzer = new StandardAnalyzer(TestLuceneVersion);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig(TestLuceneVersion, analyzer)))
        {
            writer.AddDocument(new Document
            {
                new StringField("id", "path-match", Field.Store.YES),
                new TextField("searchPath", @"C:\repos\MyTools\src", Field.Store.NO)
            });
            writer.Commit();
        }

        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);
        var hits = searcher.Search(MyTools.Plugins.FileSearcher.BuildSearchQuery(@"repos\MyTools"), 10);

        Assert.That(hits.ScoreDocs, Has.Length.EqualTo(1));
        Assert.That(searcher.Doc(hits.ScoreDocs[0].Doc).Get("id"), Is.EqualTo("path-match"));
    }

    [Test]
    public void SearchQuery_WeightsFilenameAboveDirectoryPath()
    {
        using var directory = new RamDirectory();
        using var analyzer = new StandardAnalyzer(TestLuceneVersion);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig(TestLuceneVersion, analyzer)))
        {
            writer.AddDocument(new Document
            {
                new StringField("id", "path-match", Field.Store.YES),
                new TextField("searchPath", @"C:\repos\mytools", Field.Store.NO)
            });
            writer.AddDocument(new Document
            {
                new StringField("id", "filename-match", Field.Store.YES),
                new StringField("searchFilename", "mytools", Field.Store.NO),
                new TextField("searchPossibles", "mytools", Field.Store.NO),
                new TextField("searchPath", @"C:\apps", Field.Store.NO)
            });
            writer.Commit();
        }

        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);
        var hits = searcher.Search(MyTools.Plugins.FileSearcher.BuildSearchQuery("mytools"), 10);

        Assert.That(hits.ScoreDocs, Has.Length.EqualTo(2));
        Assert.That(searcher.Doc(hits.ScoreDocs[0].Doc).Get("id"), Is.EqualTo("filename-match"));
        Assert.That(hits.ScoreDocs[0].Score, Is.GreaterThan(hits.ScoreDocs[1].Score));
    }

    [TestCase("scratch.tmp", false)]
    [TestCase("src/node_modules", true)]
    [TestCase("src/node_modules/package.json", false)]
    [TestCase("work/tmp", true)]
    [TestCase("work/tmp/result.txt", false)]
    [TestCase("User/AppData", true)]
    [TestCase("User/AppData/settings.json", false)]
    [TestCase("src/cache/data.bin", false)]
    public void IgnoreMatcher_MatchesDefaultFileAndDirectoryPatterns(string path, bool isDirectory)
    {
        var patterns = MyTools.Plugins.FileSearcher.ReadIgnorePatterns(
            MyTools.Plugins.FileSearcher.CreateDefaultIgnorePatterns());
        var matcher = new FileSearchIgnoreMatcher(patterns);

        Assert.That(matcher.IsIgnored(path, isDirectory), Is.True);
    }

    [Test]
    public void ScopedIgnoreRules_ApplyFromTheirDirectoryAndSupportNegation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"FileSearcherIgnore-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "src");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllLines(Path.Combine(nested, ".gitignore"), ["*.log", "!keep.log"]);
            var rules = FileSearchIgnoreMatcher.ScopedRules.Empty.AddFromDirectory(nested, "src");

            Assert.Multiple(() =>
            {
                Assert.That(rules.IsIgnored("src/error.log", false), Is.True);
                Assert.That(rules.IsIgnored("src/keep.log", false), Is.False);
                Assert.That(rules.IsIgnored("other/error.log", false), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void EnumerateIndexableFiles_AppliesGlobalAndIgnoreFileRules()
    {
        var root = Path.Combine(Path.GetTempPath(), $"FileSearcherIndex-{Guid.NewGuid():N}");
        var modules = Path.Combine(root, "node_modules");
        Directory.CreateDirectory(modules);
        try
        {
            File.WriteAllText(Path.Combine(root, "keep.txt"), "keep");
            File.WriteAllText(Path.Combine(root, "drop.tmp"), "drop");
            File.WriteAllText(Path.Combine(root, "trace.log"), "drop");
            File.WriteAllText(Path.Combine(root, "keep.log"), "keep");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "*.log\n!keep.log\n");
            File.WriteAllText(Path.Combine(modules, "package.json"), "{}");

            using var cache = new MemoryCache(new MemoryCacheOptions());
            using var searcher = new MyTools.Plugins.FileSearcher(
                NullLogger<MyTools.Plugins.FileSearcher>.Instance, cache);
            var matcher = new FileSearchIgnoreMatcher(["*.tmp", "node_modules"]);

            var files = searcher.EnumerateIndexableFiles(root, matcher, true, CancellationToken.None)
                .Select(Path.GetFileName)
                .ToArray();

            Assert.That(files, Does.Contain("keep.txt"));
            Assert.That(files, Does.Contain("keep.log"));
            Assert.That(files, Does.Not.Contain("drop.tmp"));
            Assert.That(files, Does.Not.Contain("trace.log"));
            Assert.That(files, Does.Not.Contain("package.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
