using MyTools.Common;
using MyTools.Common.Localization;
using MyTools.Plugins.Param;
using MyTools.Common.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Microsoft.Data.Sqlite;

namespace MyTools.Plugins.Test.Helpers;

[TestFixture]
public sealed class GlobalResultRankerTests
{
    private const int HighFrequencySelections = 50;
    private const int SlowPluginDelayMs = 20;
    private const int FastPluginDelayMs = 1;
    private const double ScoreTolerance = .000001;
    private string directory = null!;
    private SearchHistoryDbHelper history = null!;
    private readonly DateTime now = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);
    [SetUp] public void SetUp() {
        directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        history = new SearchHistoryDbHelper(Path.Combine(directory, "history.db"));
    }
    [TearDown] public void TearDown() => Directory.Delete(directory, true);
    private static ResultItem Item(string id, string title, string plugin = "third-party", int priority = 0, bool executable = true) =>
        new(new StringIcon("*"), title, "", ActionStringParam.From(id), priority) {
            SourcePluginId = plugin, ResultKey = id,
            AllowedActions = executable ? new[] { new ActionWithHotkey(new TestAction(), Hotkey.Enter) } : []
        };
    private sealed class TestAction : IAction {
        public string Id => "run";
        public string Name => "run";
        public string Description => "run";
        public Task<ActionResult> ExecuteAsync(IActionParams args) => Task.FromResult(ActionResult.CreateSuccess("ok"));
    }
    [TestCase("ＤＥＶ  \t test", "dev test")]
    [TestCase(" Cafe\u0301 ", "café")]
    public void QueryNormalization(string input, string expected) => Assert.That(SearchHistoryDbHelper.NormalizeQuery(input), Is.EqualTo(expected));
    [Test] public void NormalizeRepairsInvalidUtf16AndPreservesValidCharacters()
    {
        // Construct malformed strings inside the test: test discovery serializes
        // attribute arguments and replaces unpaired surrogates before execution.
        var samples = new[] {
            (Input: "\uD800 ＤＥＶ", Expected: "\uFFFD dev"),
            (Input: "test\uDC00", Expected: "test\uFFFD"),
            (Input: "\uD800\U0001F680\uDC00", Expected: "\uFFFD\U0001F680\uFFFD"),
            (Input: "\U0001F680 ＤＥＶ", Expected: "\U0001F680 dev")
        };
        foreach (var sample in samples)
            Assert.That(SearchTextMatcher.Normalize(sample.Input), Is.EqualTo(sample.Expected));
    }
    [Test] public void EmojiFilenameInitialsCanBeNormalizedAndMatched()
    {
        const string title = "\U0001F680 Launch";
        var initials = MyTools.Common.Utils.StringUtils.GetInitialsFromWords(title);
        Assert.That(SearchTextMatcher.Normalize(initials), Is.EqualTo("\U0001F680l"));
        Assert.That(new SearchTextMatcher().Match("launch", title).Tier, Is.EqualTo(SearchMatchTier.Prefix));
        Assert.That(new SearchTextMatcher().Match("missing", title).Tier, Is.EqualTo(SearchMatchTier.Fallback));
    }

    [TestCase("dev", "DEV", SearchMatchTier.Exact)]
    [TestCase("dev", "Device Manager", SearchMatchTier.Prefix)]
    [TestCase("manager", "Device Manager", SearchMatchTier.Prefix)]
    [TestCase("vice", "Device Manager", SearchMatchTier.Fuzzy)]
    [TestCase("gthb", "GitHub", SearchMatchTier.Fuzzy)]
    [TestCase("qidong", "启动开发环境", SearchMatchTier.Fuzzy)]
    [TestCase("qdkf", "启动开发环境", SearchMatchTier.Fuzzy)]
    [TestCase("manager device", "Device Manager", SearchMatchTier.Fuzzy)]
    [TestCase("xyz", "Valid domain result", SearchMatchTier.Fallback)]
    public void TitleMatch(string query, string title, SearchMatchTier tier) => Assert.That(new SearchTextMatcher().Match(query, title).Tier, Is.EqualTo(tier));
    [Test] public void TitleBandsThenSourceThenTiersAndStableKeyIgnorePriorityAndCompletionOrder() {
        var items = new[] { Item("weak", "distant environment view", priority: int.MaxValue),
            Item("file", "dev", FileSearcher.BuiltInPluginId), Item("z", "dev"), Item("a", "dev"), Item("fallback", "Remote body hit") };
        for (var i = 0; i < HighFrequencySelections; i++) history.RecordSelection("other", FileSearcher.BuiltInPluginId, "file", selectedAt: now);
        var ranker = new GlobalResultRanker(history);
        var first = ranker.Rank(items, "dev", now).Select(r => r.Item.ResultKey).ToArray();
        Assert.That(first, Is.EqualTo(new[] { "a", "z", "file", "weak", "fallback" }));
        Assert.That(ranker.Rank(items.Reverse(), "dev", now).Select(r => r.Item.ResultKey), Is.EqualTo(first));
    }
    [Test] public void OrdinaryResultsPrioritizeExactThenMatchedThenUnmatchedBeforeSource()
    {
        const string query = "vsc";
        var exact = Item("exact", query);
        var exactFile = Item("exact-file", query, FileSearcher.BuiltInPluginId);
        var prefix = Item("prefix", "vsc launcher");
        var fuzzy = Item("fuzzy", "Visual Studio Code", "applications");
        var prefixFile = Item("prefix-file", "vsc config", FileSearcher.BuiltInPluginId, int.MaxValue);
        var fuzzyFile = Item("fuzzy-file", "Visual Studio Code", FileSearcher.BuiltInPluginId);
        var unmatched = Item("unmatched", "Remote body hit", priority: int.MaxValue);
        var unmatchedFile = Item("unmatched-file", "Remote body hit", FileSearcher.BuiltInPluginId);
        var engine = Item("engine", query, GlobalResultRanker.SearchEnginePluginId, int.MaxValue);
        for (var i = 0; i < HighFrequencySelections; i++)
            history.RecordSelection("other", prefixFile.SourcePluginId, prefixFile.ResultKey, selectedAt: now);
        var expected = new[] { exact, exactFile, prefix, fuzzy, prefixFile, fuzzyFile, unmatched, unmatchedFile, engine };
        var ranker = new GlobalResultRanker(history);
        var ranks = ranker.Rank(expected.Reverse(), query, now);
        Assert.That(ranks.Select(r => r.Item), Is.EqualTo(expected));
        Assert.That(ranks.Select(r => r.TitleBand), Is.EqualTo(new[] {
            SearchTitleBand.Exact, SearchTitleBand.Exact,
            SearchTitleBand.TitleMatch, SearchTitleBand.TitleMatch, SearchTitleBand.TitleMatch, SearchTitleBand.TitleMatch,
            SearchTitleBand.Unmatched, SearchTitleBand.Unmatched, SearchTitleBand.Exact
        }));
        Assert.That(ranker.Rank(expected, query, now).Select(r => r.Item), Is.EqualTo(expected));
    }

    [TestCase("Search Google: vsc")]
    [TestCase("使用 Google 搜索：vsc")]
    [TestCase("vsc")]
    public void SearchActionsFollowApplicationsAndFilesRegardlessOfTitleMatch(string engineTitle)
    {
        const string query = "vsc";
        var app = Item("app", "Visual Studio Code", "applications");
        var file = Item("file", "Remote domain hit", FileSearcher.BuiltInPluginId);
        var engine = Item("engine", engineTitle, GlobalResultRanker.SearchEnginePluginId, int.MaxValue);
        for (var i = 0; i < HighFrequencySelections; i++)
            history.RecordSelection("other", engine.SourcePluginId, engine.ResultKey, selectedAt: now);
        var ranker = new GlobalResultRanker(history);
        var ranks = ranker.Rank(new[] { engine, file, app }, query, now);
        Assert.That(ranks.Select(r => r.Item), Is.EqualTo(new[] { app, file, engine }));
        Assert.That(ranks.Last().Group, Is.EqualTo(SearchRankGroup.FallbackAction));
        Assert.That(ranks.First().MatchTier, Is.EqualTo(SearchMatchTier.Fuzzy));
        Assert.That(ranker.Rank(new[] { app, file, engine }, query, now).Select(r => r.Item),
            Is.EqualTo(ranks.Select(r => r.Item)));
    }

    [Test] public void SearchActionReuseCanLeadAndFallsBackWhenUnavailableOrExpired()
    {
        const string query = "vsc";
        var app = Item("app", "Visual Studio Code", "applications");
        var engine = Item("engine", "Search Google: vsc", GlobalResultRanker.SearchEnginePluginId);
        history.RecordSelection(query, app.SourcePluginId, app.ResultKey, selectedAt: now.AddDays(-1));
        history.RecordSelection(query, engine.SourcePluginId, engine.ResultKey, selectedAt: now);
        var ranker = new GlobalResultRanker(history);
        var ranks = ranker.Rank(new[] { app, engine }, query, now);
        Assert.That(ranks.First().Item, Is.SameAs(engine));
        Assert.That(ranks.First().Group, Is.EqualTo(SearchRankGroup.QueryReuse));
        engine.AllowedActions = [];
        var unavailable = ranker.Rank(new[] { app, engine }, query, now);
        Assert.That(unavailable.First().Item, Is.SameAs(app));
        Assert.That(unavailable.First().Group, Is.EqualTo(SearchRankGroup.QueryReuse));
        Assert.That(unavailable.Last().Group, Is.EqualTo(SearchRankGroup.FallbackAction));
        var expired = ranker.Rank(new[] { app, engine }, query,
            now.AddDays(SearchHistoryRanker.QueryReuseValidityDays + 1));
        Assert.That(expired.Select(r => r.Group),
            Is.EqualTo(new[] { SearchRankGroup.Ordinary, SearchRankGroup.FallbackAction }));
    }

    [Test] public async Task SearchEnginePluginOrderIsPreservedWhileGlobalResultsUseFallbackGroup()
    {
        const string query = "vsc";
        var source = new[] { Item("google", "Search Google: vsc"), Item("exact", "vsc", priority: int.MaxValue) };
        var engines = new TestPlugin(GlobalResultRanker.SearchEnginePluginId, source, FastPluginDelayMs);
        var applications = new TestPlugin("applications", new[] { Item("app", "Visual Studio Code") }, SlowPluginDelayMs);
        ISearcher searcher = new Searcher(new TestRegistry(engines, applications), history, NullLogger<Searcher>.Instance);
        Assert.That((await searcher.SearchAsync(engines, query, CancellationToken.None)).Items.Select(i => i.ResultKey),
            Is.EqualTo(new[] { "google", "exact" }));
        Assert.That((await searcher.SearchAsync(null, query, CancellationToken.None)).Items.Select(i => i.ResultKey),
            Is.EqualTo(new[] { "app", "exact", "google" }));
    }

    [Test] public void LatestSuccessfulSelectionWinsAcrossTiersAndFilesAndFallsBack() {
        var exact = Item("exact", "dev");
        var old = Item("old", "启动开发环境");
        var file = Item("file", "Unrelated title", FileSearcher.BuiltInPluginId);
        for (var i = 0; i < HighFrequencySelections; i++) history.RecordSelection("dev", old.SourcePluginId, old.ResultKey, selectedAt: now.AddDays(-2));
        history.RecordSelection(" ＤＥＶ ", file.SourcePluginId, file.ResultKey, selectedAt: now.AddDays(-1));
        var ranker = new GlobalResultRanker(history);
        Assert.That(ranker.Rank(new[] { exact, old, file }, "dev", now).First().Item, Is.SameAs(file));
        file.AllowedActions = [];
        Assert.That(ranker.Rank(new[] { exact, old, file }, "dev", now).First().Item, Is.SameAs(old));
        history.RecordSelection("dev", exact.SourcePluginId, exact.ResultKey, selectedAt: now);
        Assert.That(ranker.Rank(new[] { exact, old, file }, "dev", now).First().Item, Is.SameAs(exact));
        Assert.That(ranker.Rank(new[] { exact, old, file }, "dev", now.AddDays(SearchHistoryRanker.QueryReuseValidityDays + 1)).All(r => r.Group == SearchRankGroup.Ordinary), Is.True);
        Assert.That(ranker.Rank(new[] { old, file }, "dev-other", now).All(r => r.Group == SearchRankGroup.Ordinary), Is.True);
    }
    [Test] public void LegacyQueryNormalizationMergesEquivalentQueriesWithoutChangingIdentity() {
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(directory, "history.db")};Pooling=False")) {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO search_selection_history VALUES (@query, 'p', 'stable', 2, @time)";
            cmd.Parameters.AddWithValue("@query", "ＤＥＶ  test");
            cmd.Parameters.AddWithValue("@time", now.AddDays(-1).ToString("o"));
            cmd.ExecuteNonQuery();
        }
        history.RecordSelection("dev test", "p", "stable", selectedAt: now);
        var reopened = new SearchHistoryDbHelper(Path.Combine(directory, "history.db"));
        var record = reopened.GetQuerySelections(" DEV   TEST ").Single();
        Assert.That(record.Count, Is.EqualTo(3));
        Assert.That(record.ResultKey, Is.EqualTo("stable"));
        Assert.That(record.LastSelectedAt, Is.EqualTo(now));
        Assert.That(new SearchHistoryDbHelper(Path.Combine(directory, "history.db")).GetQuerySelections("dev test").Single().Count, Is.EqualTo(3));
    }
    [Test] public void UsageAggregatesAllQueriesForOneResultAndDecaysWithBoundedScore() {
        history.RecordSelection("dev", "p", "one", selectedAt: now);
        history.RecordSelection("启动", "p", "one", selectedAt: now);
        history.RecordSelection("dev", "p", "other", selectedAt: now);
        var ranker = new SearchHistoryRanker();
        var records = history.GetResultUsage().Where(r => r.ResultKey == "one").ToArray();
        Assert.That(records.Sum(r => r.Count), Is.EqualTo(2));
        var value = ranker.UsageValue(records, now);
        Assert.That(value, Is.EqualTo(1 - Math.Exp(-.2)).Within(ScoreTolerance));
        Assert.That(ranker.UsageValue(records, now.AddDays(SearchHistoryRanker.UsageDecayDays * 3)), Is.LessThan(value));
        Assert.That(ranker.UsageValue(new[] { new SearchUsage("p", "one", long.MaxValue, now) }, now), Is.InRange(0, 1));
    }
    private sealed class TestPlugin(string id, ResultItem[] items, int delay) : PluginBase {
        public override PluginId PluginId => new(id);
        public override string Name => id;
        public override string Description => id;
        public override bool IsGlobalSearchPlugin => true;
        public override List<IActionWithHotkey> Actions => [new ActionWithHotkey(new TestAction(), Hotkey.Enter)];
        public override async Task<Result> SearchAsync(string query, CancellationToken token, SearchOptions? options = null) {
            await Task.Delay(delay, token);
            return Result.CreateOrderedSuccessResult(items);
        }
    }
    private sealed class TestRegistry(params IPlugin[] plugins) : IGlobalSearchRegistry {
        public IEnumerable<IPlugin> Plugins => plugins;
        public void Register(IPlugin plugin) => throw new NotSupportedException();
        public void UnregisterPlugin(IPlugin plugin) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }
    [Test] public async Task SearcherPreservesPluginOrderAndUnifiesGlobalOrder() {
        var source = new[] { Item("weak", "distant environment view", priority: int.MaxValue), Item("exact", "dev") };
        var plugin = new TestPlugin("script", source, SlowPluginDelayMs);
        var file = new TestPlugin(FileSearcher.BuiltInPluginId, new[] { Item("file", "dev", priority: int.MaxValue) }, FastPluginDelayMs);
        ISearcher searcher = new Searcher(new TestRegistry(file, plugin), history, NullLogger<Searcher>.Instance);
        history.RecordSelection("dev", "script", "exact");
        Assert.That((await searcher.SearchAsync(plugin, "dev", CancellationToken.None)).Items.Select(i => i.ResultKey),
            Is.EqualTo(new[] { "weak", "exact" }));
        Assert.That((await searcher.SearchAsync(null, "dev", CancellationToken.None)).Items.Select(i => i.ResultKey),
            Is.EqualTo(new[] { "exact", "file", "weak" }));
        Assert.That(source[0].SourcePluginId, Is.EqualTo("third-party"));
    }
    private sealed class TestLocalization : ILocalizationService {
        public string CurrentLocale { get; set; } = "zh-CN";
        public event EventHandler<LocaleChangedEventArgs>? LocaleChanged { add {} remove {} }
        public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null) =>
            CurrentLocale == "zh-CN" ? "启动开发环境" : defaultValue;
    }
    [Test] public void CurrentDisplayLocaleDeterminesTitleMatch() {
        var locale = new TestLocalization();
        var item = new ResultItem(new StringIcon("*"), new LocalizedMessage("Test.Title", "Device Manager"),
            new LocalizedMessage("Test.Subtitle", ""), ActionStringParam.From("id")) { SourcePluginId = "p", ResultKey = "id" };
        var ranker = new GlobalResultRanker(history, locale);
        Assert.That(ranker.Rank(new[] { item }, "qidong", now).Single().MatchTier, Is.EqualTo(SearchMatchTier.Fuzzy));
        locale.CurrentLocale = "en-US";
        Assert.That(ranker.Rank(new[] { item }, "qidong", now).Single().MatchTier, Is.EqualTo(SearchMatchTier.Fallback));
        Assert.That(ranker.Rank(new[] { item }, "dev", now).Single().MatchTier, Is.EqualTo(SearchMatchTier.Prefix));
    }
    [Test] public void ResultConstructionPreservesRankedInputDespitePriority() {
        var items = new[] { Item("first", "First"), Item("second", "Second", priority: int.MaxValue) };
        Assert.That(Result.CreateOrderedSuccessResult(items).Items, Is.EqualTo(items));
    }
}
