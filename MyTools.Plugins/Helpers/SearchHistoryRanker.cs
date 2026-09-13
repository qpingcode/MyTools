namespace MyTools.Plugins;

public sealed record SearchUsage(string PluginId, string ResultKey, long Count, DateTime LastSelectedAt);

public sealed class SearchHistoryRanker
{
    private const long MinimumReuseSelectionCount = 1;
    public const double UsageDecayDays = 30;
    public const double UsageSmoothingCount = 10;
    public const double QueryReuseValidityDays = 14;
    public double UsageValue(IEnumerable<SearchUsage> records, DateTime now) =>
        1 - Math.Exp(-records.Sum(r => r.Count * Math.Exp(-Age(r, now) / UsageDecayDays)) / UsageSmoothingCount);
    public bool CanReuse(SearchUsage record, DateTime now) => record.Count >= MinimumReuseSelectionCount && Age(record, now) <= QueryReuseValidityDays;
    private static double Age(SearchUsage record, DateTime now) => Math.Max(0, (now.ToUniversalTime() - record.LastSelectedAt.ToUniversalTime()).TotalDays);
}
