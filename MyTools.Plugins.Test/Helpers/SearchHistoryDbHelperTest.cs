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
            SearchFrom = SearchFrom.Plugin, AllowedActions = plugin.Actions
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
            Assert.That(item.AllowedActions.Select(action => action.Hotkey),
                Is.EqualTo(new[] { Hotkey.Enter, Hotkey.Ctrl(HotkeyKey.O) }));
            Assert.That(plugin.SearchCalls, Is.Zero);
            Assert.That(reopened.GetRecentSelections().Single().SelectionCount, Is.EqualTo(2));
        });
        var outcome = await item.AllowedActions.First().ExecuteAsync(item.Args);
        Assert.That(outcome.Success, Is.True);
        Assert.That(plugin.SearchCalls, Is.Zero);
        Assert.That(plugin.ExecutedValue, Is.EqualTo("fav"));
    }

    [Test]
    public void RecentSelections_UsesNewestFirstAndDeduplicatesAcrossQueries()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var now = DateTime.UtcNow;
        helper.RecordSelection("old", "plugin", "old", selectedAt: now.AddDays(-30));
        helper.RecordSelection("first", "plugin", "frequent", selectedAt: now.AddDays(-29));
        helper.RecordSelection("MixedCase", "plugin", "frequent", SearchFrom.Plugin, now.AddDays(-2));
        helper.RecordSelection("earlier", "plugin", "earlier", selectedAt: now.AddHours(-1));
        helper.RecordSelection("latest", "plugin", "latest", selectedAt: now);
        var history = helper.GetRecentSelections(now);
        Assert.That(history.Select(item => item.ResultKey), Is.EqualTo(new[] { "latest", "earlier", "frequent" }));
        Assert.That(history.Last().SelectionCount, Is.EqualTo(2));
        Assert.That(history.Last().Query, Is.EqualTo("MixedCase"));
        Assert.That(history.Last().SearchFrom, Is.EqualTo(SearchFrom.Plugin));
    }

    [Test]
    public async Task HomePage_ExcludesUnavailablePluginsWithoutQuerying()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var disabled = new FakePlugin();
        disabled.Disable();
        helper.RecordSelection("q", disabled.PluginId.Value, "fav", snapshot: CreateSnapshot(disabled, "Disabled", "fav"));
        helper.RecordSelection("q", "uninstalled", "fav", snapshot: CreateSnapshot(disabled, "Removed", "fav"));
        var searcher = new Searcher(new FakeGlobalSearchRegistry(disabled), helper, NullLogger<Searcher>.Instance);
        Assert.That((await ((ISearcher)searcher).SearchAsync(null, "", CancellationToken.None)).Items, Is.Empty);
        Assert.That(disabled.SearchCalls, Is.Zero);
    }

    [Test]
    public async Task HomePage_ExcludesPluginsNotIncludedInGlobalSearch()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new FakePlugin(isGlobalSearchPlugin: false);
        var searcher = new Searcher(new FakeGlobalSearchRegistry(plugin), helper, NullLogger<Searcher>.Instance);
        var pluginResult = await ((ISearcher)searcher).SearchAsync(plugin, "q", CancellationToken.None);

        helper.RecordSelection(pluginResult.Items.Single(item => item.ResultKey == "fav"));

        var result = await ((ISearcher)searcher).SearchAsync(null, "", CancellationToken.None);

        Assert.That(result.Items, Is.Empty);
        Assert.That(helper.GetRecentSelections(), Is.Empty);
        Assert.That(helper.GetSelectionBoosts("q"), Contains.Key(
            SearchHistoryDbHelper.CombineKey(plugin.PluginId.Value, "fav")));
        Assert.That(plugin.SearchCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task HomePage_UsesPluginSearchResultsWhenHistoryIsEmpty()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new FakePlugin(pluginId: "plugin-search", suggestionCount: 12);
        var searcher = new Searcher(new FakeGlobalSearchRegistry(plugin), helper, NullLogger<Searcher>.Instance);

        var result = await ((ISearcher)searcher).SearchAsync(null, "", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(item => item.ResultKey),
                Is.EqualTo(Enumerable.Range(1, 10).Select(index => $"suggestion-{index}")));
            Assert.That(result.Items, Has.All.Property(nameof(ResultItem.SearchFrom)).EqualTo(SearchFrom.Plugin));
            Assert.That(plugin.SearchCalls, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HomePage_ClickRechecksPluginAndRemovesUnavailableResult(bool disableAfterDisplay)
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new FakePlugin();
        var missingSnapshot = CreateSnapshot(plugin, "Missing", "missing") with
        {
            Actions =
            [
                new SearchActionSnapshot(
                    "removed-action", "Removed", "Removed", HotkeyKey.Enter, HotkeyModifiers.None, false)
            ]
        };
        helper.RecordSelection("q", plugin.PluginId.Value, "missing", snapshot: missingSnapshot);
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
    public async Task HomePage_IgnoresLegacySnapshots()
    {
        var helper = new SearchHistoryDbHelper(_dbPath);
        var plugin = new FakePlugin();
        helper.RecordSelection("q", plugin.PluginId.Value, "fav",
            snapshot: new("Legacy", "", "emoji", ""));
        var searcher = new Searcher(new FakeGlobalSearchRegistry(plugin), helper, NullLogger<Searcher>.Instance);

        Assert.That((await ((ISearcher)searcher).SearchAsync(null, "", CancellationToken.None)).Items, Is.Empty);
        Assert.That(plugin.SearchCalls, Is.Zero);
    }

    private static SearchResultSnapshot CreateSnapshot(FakePlugin plugin, string title, string value) =>
        SearchResultSnapshot.Create(new ResultItem(
            StringIcon.Empty, title, string.Empty, ActionStringParam.From(value))
        {
            AllowedActions = plugin.Actions
        });

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

    private sealed class FakePlugin(
        bool isGlobalSearchPlugin = true,
        string? pluginId = null,
        int? suggestionCount = null) : PluginBase
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
        private sealed class SecondaryAction : IAction
        {
            public string Name => "Secondary";
            public string Description => "Secondary";
            public Task<ActionResult> ExecuteAsync(IActionParams args) =>
                Task.FromResult(ActionResult.CreateSuccess(""));
        }
        public override PluginId PluginId => new(pluginId ?? GetType().FullName!);

        public override string Name => "Fake";
        public override string Description => "Fake";
        public override List<IActionWithHotkey> Actions =>
        [
            new ActionWithHotkey(new TestAction(this), Hotkey.Enter),
            new ActionWithHotkey(new SecondaryAction(), Hotkey.Ctrl(HotkeyKey.O))
        ];
        public override bool IsGlobalSearchPlugin => isGlobalSearchPlugin;

        public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            SearchCalls++;
            if (suggestionCount.HasValue)
            {
                var suggestions = Enumerable.Range(1, suggestionCount.Value)
                    .Select(index => new ResultItem(
                        StringIcon.Empty,
                        $"suggestion-{index}",
                        $"suggestion-{index}",
                        ActionStringParam.From($"suggestion-{index}"),
                        100)
                    {
                        ResultKey = $"suggestion-{index}"
                    });
                return Task.FromResult(Result.CreateSuccessResult(suggestions));
            }

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
