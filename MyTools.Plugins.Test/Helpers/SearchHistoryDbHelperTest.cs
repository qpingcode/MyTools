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
    public async Task HomePage_ReadsSelectedSnapshotsWithoutAnyPluginQueries()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new FakePlugin();
        helper.RecordSearch("keyword only");
        var searcher = new Searcher(new FakeGlobalSearchRegistry(plugin), helper, NullLogger<Searcher>.Instance);
        Assert.That((await ((ISearcher)searcher).SearchAsync(null, "", CancellationToken.None)).Items, Is.Empty);
        var selected = new ResultItem(new StringIcon("★"), "Selected result", "Original subtitle", ActionStringParam.From("fav"))
        {
            SourcePluginId = plugin.PluginId.Value, ResultKey = "fav", SearchQuery = "MixedCase",
            SearchFrom = SearchFrom.Plugin
        };
        helper.RecordSelection(selected);
        helper.RecordSelection(selected);
        var reopened = new SearchHistoryDbHelper(_dbPath);
        searcher = new Searcher(new FakeGlobalSearchRegistry(plugin), reopened, NullLogger<Searcher>.Instance);
        var result = await ((ISearcher)searcher).SearchAsync(null, "  ", CancellationToken.None);
        var item = result.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.Title, Is.EqualTo("Selected result"));
            Assert.That(item.SubTitle, Is.EqualTo("Original subtitle"));
            Assert.That(((StringIcon)item.Icon).Emoji, Is.EqualTo("★"));
            Assert.That(item.SearchQuery, Is.EqualTo("MixedCase"));
            Assert.That(item.SearchFrom, Is.EqualTo(SearchFrom.Plugin));
            Assert.That(item.AllowedActions.Single().Hotkey, Is.EqualTo(Hotkey.Enter));
            Assert.That(plugin.SearchCalls, Is.Zero);
            Assert.That(reopened.GetRecentSelections().Single().SelectionCount, Is.EqualTo(2));
        });
        var outcome = await item.AllowedActions.Single().ExecuteAsync(item.Args);
        Assert.That(outcome.Success, Is.True);
        Assert.That(plugin.SearchCalls, Is.EqualTo(1));
        Assert.That(plugin.ExecutedValue, Is.EqualTo("fav"));
    }

    [Test]
    public void RecentSelections_CountsThirtyDaysAndDeduplicatesAcrossQueries()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var now = DateTime.UtcNow;
        helper.RecordSelection("old", "plugin", "old", selectedAt: now.AddDays(-30));
        helper.RecordSelection("first", "plugin", "frequent", selectedAt: now.AddDays(-29));
        helper.RecordSelection("MixedCase", "plugin", "frequent", SearchFrom.Plugin, now.AddDays(-2));
        helper.RecordSelection("earlier", "plugin", "earlier", selectedAt: now.AddHours(-1));
        helper.RecordSelection("latest", "plugin", "latest", selectedAt: now);
        var history = helper.GetRecentSelections(now);
        Assert.That(history.Select(item => item.ResultKey), Is.EqualTo(new[] { "frequent", "latest", "earlier" }));
        Assert.That(history.First().SelectionCount, Is.EqualTo(2));
        Assert.That(history.First().Query, Is.EqualTo("MixedCase"));
        Assert.That(history.First().SearchFrom, Is.EqualTo(SearchFrom.Plugin));
    }

    [Test]
    public async Task HomePage_ExcludesUnavailablePluginsWithoutQuerying()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var disabled = new FakePlugin();
        disabled.Disable();
        helper.RecordSelection("q", disabled.PluginId.Value, "fav", snapshot: new("Disabled", "", "emoji", ""));
        helper.RecordSelection("q", "uninstalled", "fav", snapshot: new("Removed", "", "emoji", ""));
        var searcher = new Searcher(new FakeGlobalSearchRegistry(disabled), helper, NullLogger<Searcher>.Instance);
        Assert.That((await ((ISearcher)searcher).SearchAsync(null, "", CancellationToken.None)).Items, Is.Empty);
        Assert.That(disabled.SearchCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HomePage_ClickRechecksPluginAndRemovesUnavailableResult(bool disableAfterDisplay)
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new FakePlugin();
        helper.RecordSelection("q", plugin.PluginId.Value, "missing", snapshot: new("Missing", "", "emoji", ""));
        var searcher = new Searcher(new FakeGlobalSearchRegistry(plugin), helper, NullLogger<Searcher>.Instance);
        var item = (await ((ISearcher)searcher).SearchAsync(null, "", CancellationToken.None)).Items.Single();
        if (disableAfterDisplay) plugin.Disable();
        var outcome = await item.AllowedActions.Single().ExecuteAsync(item.Args);
        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.ActionType, Is.EqualTo(ActionTypeEnum.Refresh));
        Assert.That(plugin.ExecutedValue, Is.Null);
        Assert.That((await ((ISearcher)searcher).SearchAsync(null, "", CancellationToken.None)).Items, Is.Empty);
        Assert.That(plugin.SearchCalls, Is.EqualTo(disableAfterDisplay ? 0 : 1));
    }

    [Test]
    public async Task HomePage_DoesNotRestoreLegacyHistoryWithoutSnapshots()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new FakePlugin();
        helper.RecordSelection("q", plugin.PluginId.Value, "fav");
        var searcher = new Searcher(new FakeGlobalSearchRegistry(plugin), helper, NullLogger<Searcher>.Instance);
        Assert.That((await ((ISearcher)searcher).SearchAsync(null, "", CancellationToken.None)).Items, Is.Empty);
        Assert.That(plugin.SearchCalls, Is.Zero);
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
        helper.RecordSelection("calc", plugin.PluginId.Value, "fav");

        var searcher = new Searcher(new FakeGlobalSearchRegistry(plugin), helper, NullLogger<Searcher>.Instance);
        var result = await ((ISearcher)searcher).SearchAsync(null, "calc", CancellationToken.None);

        Assert.That(result.Items.First().ResultKey, Is.EqualTo("fav"));
    }

    [Test]
    public async Task Searcher_PreservesNewestFirstForItemsThatIgnoreSelectionHistoryBoost()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new ChronologicalPlugin();
        helper.RecordSelection(string.Empty, plugin.PluginId.Value, "old");

        var searcher = new Searcher(
            new FakeGlobalSearchRegistry(plugin),
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
        public int SearchCalls { get; private set; }
        public string? ExecutedValue { get; private set; }
        public void Disable() => pluginState.IsEnabled = false;
        private sealed class TestAction(FakePlugin plugin) : IAction
        {
            public string Name => "Execute";
            public string Description => "Execute";
            public Task<ActionResult> ExecuteAsync(IActionParams args)
            {
                plugin.ExecutedValue = ((IActionStringParam)args).GetValue();
                return Task.FromResult(ActionResult.CreateSuccess(""));
            }
        }
        public override PluginId PluginId => new(GetType().FullName!);

        public override string Name => "Fake";
        public override string Description => "Fake";
        public override List<IActionWithHotkey> Actions => [new ActionWithHotkey(new TestAction(this), Hotkey.Enter)];
        public override bool IsGlobalSearchPlugin => true;

        public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            SearchCalls++;
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
        public override PluginId PluginId => new(GetType().FullName!);
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
        public override PluginId PluginId => new(GetType().FullName!);
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
