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
        public override List<IActionWithCommand> Actions => [];
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
}
