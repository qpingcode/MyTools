using MyTools.Common;
using MyTools.Common.Localization;

namespace MyTools.Plugins;

/// <summary>Reuse precedes ordinary candidates; external search actions are the final fallback.</summary>
public enum SearchRankGroup
{
    QueryReuse,
    Ordinary,
    FallbackAction
}

/// <summary>Title evidence is compared before source preference.</summary>
public enum SearchTitleBand
{
    Exact,
    TitleMatch,
    Unmatched
}

public sealed record GlobalResultRank(ResultItem Item, SearchRankGroup Group, SearchMatchTier MatchTier, bool IsFile,
    double TextQuality, double UsageValue, double Score, string StableKey,
    bool ReuseEligible, DateTime? LastSelectedAt)
{
    public SearchTitleBand TitleBand => MatchTier switch
    {
        SearchMatchTier.Exact => SearchTitleBand.Exact,
        SearchMatchTier.Fallback => SearchTitleBand.Unmatched,
        _ => SearchTitleBand.TitleMatch
    };
}

public sealed class GlobalResultRanker(SearchHistoryDbHelper history, ILocalizationService? localization = null)
{
    internal const string SearchEnginePluginId = "search-engine";

    private const double TextQualityWeight = 70;
    private const double UsageValueWeight = 5;

    private readonly SearchTextMatcher matcher = new();
    private readonly SearchHistoryRanker historyRanker = new();
    public IReadOnlyList<GlobalResultRank> Rank(IEnumerable<ResultItem> items, string query, DateTime? timestamp = null)
    {
        var now = timestamp ?? DateTime.UtcNow;
        var selections = history.GetQuerySelections(query).ToDictionary(r => SearchHistoryDbHelper.CombineKey(r.PluginId, r.ResultKey));
        var usage = history.GetResultUsage().ToLookup(r => SearchHistoryDbHelper.CombineKey(r.PluginId, r.ResultKey));
        var ranks = items.Select(item => {
            var key = SearchHistoryDbHelper.CombineKey(item.SourcePluginId, item.ResultKey);
            var title = localization != null && item.LocalizedTitle != null ? item.LocalizedTitle.Resolve(localization) : item.Title;
            var match = matcher.Match(query, title);
            var selected = selections.GetValueOrDefault(key);
            var eligible = !item.IgnoreSelectionHistoryBoost && item.AllowedActions.Any()
                && selected != null && historyRanker.CanReuse(selected, now);
            var frequency = item.IgnoreSelectionHistoryBoost ? 0 : historyRanker.UsageValue(usage[key], now);
            // Generated search-action titles contain the query; that is not evidence
            // of a discovered candidate and must not outrank ordinary results.
            var group = item.SourcePluginId == SearchEnginePluginId
                ? SearchRankGroup.FallbackAction
                : SearchRankGroup.Ordinary;
            return new GlobalResultRank(item, group, match.Tier, item.SourcePluginId == FileSearcher.BuiltInPluginId,
                match.Quality, frequency, match.Quality * TextQualityWeight + frequency * UsageValueWeight, key, eligible, selected?.LastSelectedAt);
        }).ToList();
        var reused = ranks.Where(r => r.ReuseEligible).OrderByDescending(r => r.LastSelectedAt)
            .ThenBy(r => r.StableKey, StringComparer.Ordinal).FirstOrDefault();
        return ranks.Select(r => ReferenceEquals(r, reused) ? r with { Group = SearchRankGroup.QueryReuse } : r)
            .OrderBy(r => r.Group).ThenBy(r => r.TitleBand)
            .ThenBy(r => r.IsFile).ThenBy(r => r.MatchTier)
            .ThenByDescending(r => r.Score).ThenBy(r => r.StableKey, StringComparer.Ordinal).ToArray();
    }
}
