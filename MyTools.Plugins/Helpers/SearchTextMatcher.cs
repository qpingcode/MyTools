using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using ToolGood.Words.Pinyin;

namespace MyTools.Plugins;

/// <summary>Declaration order is the global ranking precedence.</summary>
public enum SearchMatchTier
{
    Exact = 1,
    Prefix,
    Fuzzy,
    Fallback
}

public readonly record struct SearchTextMatch(SearchMatchTier Tier, double Quality);

public sealed class SearchTextMatcher
{
    private const int TitleVariantCacheLimit = 4096;
    private const double SubstringBaseQuality = .7;
    private const double SubstringCoverageWeight = .3;
    private const double TransliterationQualityFactor = .85;
    private const double MultiwordBaseQuality = .65;
    private const double MultiwordCoverageWeight = .25;
    private const double ContinuityWeight = .4;
    private const double FuzzyCoverageWeight = .3;
    private const double WordBoundaryWeight = .2;

    private readonly ConcurrentDictionary<string, string[]> cache = new(StringComparer.Ordinal);
    public static string Normalize(string? text) => Regex.Replace(
        NormalizeUnicode(text ?? string.Empty).ToLowerInvariant(), @"\s+", " ").Trim();

    private static string NormalizeUnicode(string text)
    {
        try
        {
            return text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // External text may contain unpaired surrogates. Rune enumeration replaces
            // only invalid sequences, preserving complete supplementary characters.
            var repaired = new StringBuilder(text.Length);
            foreach (var rune in text.EnumerateRunes()) repaired.Append(rune.ToString());
            return repaired.ToString().Normalize(NormalizationForm.FormKC);
        }
    }

    public SearchTextMatch Match(string query, string title)
    {
        var needle = Normalize(query);
        var target = Normalize(title);
        if (needle.Length == 0) return new(SearchMatchTier.Fallback, 0);
        if (target == needle) return new(SearchMatchTier.Exact, 1);
        var index = target.IndexOf(needle, StringComparison.Ordinal);
        if (index >= 0)
        {
            var boundary = index == 0 || !char.IsLetterOrDigit(target[index - 1]);
            return new(boundary ? SearchMatchTier.Prefix : SearchMatchTier.Fuzzy, Math.Clamp(SubstringBaseQuality + SubstringCoverageWeight * needle.Length / Math.Max(1.0, target.Length), 0, 1));
        }
        var best = Fuzzy(needle, target);
        // Bound the cache; keys include the resolved display title, so locale changes cannot reuse stale text.
        if (cache.Count > TitleVariantCacheLimit) cache.Clear();
        var variants = cache.GetOrAdd(target, value => new[] {
            Normalize(WordsHelper.GetPinyin(value)), Normalize(WordsHelper.GetFirstPinyin(value)),
            string.Concat(Regex.Matches(value, @"[\p{L}\p{N}]+").Select(m => m.Value.EnumerateRunes().First().ToString())) });
        foreach (var variant in variants) best = Math.Max(best, TransliterationQualityFactor * Fuzzy(needle, variant));
        return best > 0 ? new(SearchMatchTier.Fuzzy, best) : new(SearchMatchTier.Fallback, 0);
    }

    private static double Fuzzy(string needle, string target)
    {
        var terms = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 1 && terms.All(t => target.Contains(t, StringComparison.Ordinal)))
            return MultiwordBaseQuality + MultiwordCoverageWeight * Math.Min(1, terms.Sum(t => t.Length) / Math.Max(1.0, target.Length));
        var position = 0;
        var first = -1;
        var last = -1;
        foreach (var c in needle)
        {
            var found = target.IndexOf(c, position);
            if (found < 0) return 0;
            if (first < 0) first = found;
            last = found;
            position = found + 1;
        }
        return ContinuityWeight * needle.Length / (last - first + 1.0)
            + FuzzyCoverageWeight * needle.Length / Math.Max(1.0, target.Length)
            + WordBoundaryWeight * (first == 0 || !char.IsLetterOrDigit(target[first - 1]) ? 1 : 0);
    }
}
