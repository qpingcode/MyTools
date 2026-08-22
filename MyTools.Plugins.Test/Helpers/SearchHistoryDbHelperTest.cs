using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Plugins.Param;

namespace MyTools.Plugins.Test.Helpers;

[TestFixture]
public class SearchHistoryDbHelperTest
{
    private string _tempDirectory = null!;
    private string _dbPath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _dbPath = Path.Combine(_tempDirectory, "search_history.db");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Test]
    public void GetSelectionBoosts_PrefersExactQueryOverPrefixQuery()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        helper.RecordSelection("git", "plugin-a", "result-1");
        helper.RecordSelection("git", "plugin-a", "result-1");
        helper.RecordSelection("github", "plugin-a", "result-2");

        var boosts = helper.GetSelectionBoosts("git");

        Assert.That(boosts[SearchHistoryDbHelper.CombineKey("plugin-a", "result-1")], Is.GreaterThan(boosts[SearchHistoryDbHelper.CombineKey("plugin-a", "result-2")]));
    }

    [Test]
    public async Task Searcher_ReordersResultsUsingSelectionHistory()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new FakePlugin();
        helper.RecordSelection("calc", plugin.PluginId, "fav");

        var searcher = new Searcher(new FakeGlobalSearchRegistry(plugin), new MemoryCache(new MemoryCacheOptions()), helper, NullLogger<Searcher>.Instance);
        var result = await ((ISearcher)searcher).SearchAsync(null, "calc", CancellationToken.None);

        Assert.That(result.Items.First().ResultKey, Is.EqualTo("fav"));
    }

    [Test]
    public async Task Searcher_PreservesNewestFirstForItemsThatIgnoreSelectionHistoryBoost()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new ChronologicalPlugin();
        helper.RecordSelection(string.Empty, plugin.PluginId, "old");

        var searcher = new Searcher(
            new FakeGlobalSearchRegistry(plugin),
            new MemoryCache(new MemoryCacheOptions()),
            helper,
            NullLogger<Searcher>.Instance);
        var result = await ((ISearcher)searcher).SearchAsync(plugin, string.Empty, CancellationToken.None);

        Assert.That(result.Items.Select(item => item.ResultKey), Is.EqualTo(new[] { "new", "old" }));
    }

    [Test]
    public async Task Searcher_PreservesPluginEmptyState()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new EmptyStatePlugin();
        var searcher = new Searcher(
            new FakeGlobalSearchRegistry(plugin),
            new MemoryCache(new MemoryCacheOptions()),
            helper,
            NullLogger<Searcher>.Instance);

        var result = await ((ISearcher)searcher).SearchAsync(plugin, string.Empty, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.EmptyStateTitle, Is.EqualTo("Nothing here"));
            Assert.That(result.EmptyStateDescription, Is.EqualTo("Copy something"));
        });
    }

    private sealed class FakeGlobalSearchRegistry(params IPlugin[] plugins) : IGlobalSearchRegistry
    {
        public IEnumerable<IPlugin> Plugins { get; } = plugins;

        public void Register(IPlugin plugin)
        {
        }

        public void UnregisterPlugin(IPlugin plugin)
        {
        }

        public void Clear()
        {
        }
    }

    private sealed class FakePlugin : PluginBase
    {
        public override string PluginId => GetType().FullName!;

        public override string Name => "Fake";
        public override string Description => "Fake";
        public override List<IActionWithHotkey> Actions => [];
        public override bool IsGlobalSearchPlugin => true;

        public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            var results = new[]
            {
                new ResultItem(StringIcon.Empty, "default", "default", ActionStringParam.From("default"), 100)
                {
                    ResultKey = "default"
                },
                new ResultItem(StringIcon.Empty, "fav", "fav", ActionStringParam.From("fav"), 1)
                {
                    ResultKey = "fav"
                }
            };

            return Task.FromResult(Result.CreateSuccessResult(results));
        }
    }

    private sealed class ChronologicalPlugin : PluginBase
    {
        public override string PluginId => GetType().FullName!;
        public override string Name => "Chronological";
        public override string Description => "Chronological";
        public override List<IActionWithHotkey> Actions => [];
        public override bool IsGlobalSearchPlugin => false;

        public override Task<Result> SearchAsync(
            string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            var now = DateTime.UtcNow;
            var results = new[]
            {
                new ResultItem(StringIcon.Empty, "new", "new", ActionStringParam.From("new"), 100)
                {
                    ResultKey = "new",
                    CreatedAt = now,
                    IgnoreSelectionHistoryBoost = true
                },
                new ResultItem(StringIcon.Empty, "old", "old", ActionStringParam.From("old"), 100)
                {
                    ResultKey = "old",
                    CreatedAt = now.AddMinutes(-1),
                    IgnoreSelectionHistoryBoost = true
                }
            };

            return Task.FromResult(Result.CreateSuccessResult(results));
        }
    }

    private sealed class EmptyStatePlugin : PluginBase
    {
        public override string PluginId => GetType().FullName!;
        public override string Name => "Empty";
        public override string Description => "Empty";
        public override List<IActionWithHotkey> Actions => [];

        public override Task<Result> SearchAsync(
            string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            return Task.FromResult(Result.CreateSuccessResult(
                Array.Empty<ResultItem>(), "Nothing here", "Copy something"));
        }
    }
}
