using System.IO;
using System.Text.RegularExpressions;

namespace MyTools.Plugins;

internal sealed class FileSearchIgnoreMatcher
{
    private readonly IReadOnlyList<GlobPattern> patterns;

    public FileSearchIgnoreMatcher(IEnumerable<string> patterns)
    {
        this.patterns = patterns
            .Select(pattern => pattern.Trim())
            .Where(pattern => pattern.Length > 0 && !pattern.StartsWith('#'))
            .Select(pattern => new GlobPattern(pattern))
            .ToArray();
    }

    public bool IsIgnored(string relativePath, bool isDirectory) =>
        patterns.Any(pattern => pattern.IsMatch(relativePath, isDirectory));

    internal sealed class ScopedRules
    {
        private readonly IReadOnlyList<ScopedRule> rules;

        private ScopedRules(IReadOnlyList<ScopedRule> rules)
        {
            this.rules = rules;
        }

        public static ScopedRules Empty { get; } = new([]);

        public ScopedRules AddFromDirectory(string directory, string baseRelativePath)
        {
            var additions = new List<ScopedRule>();
            foreach (var fileName in new[] { ".gitignore", ".ignore" })
            {
                var path = Path.Combine(directory, fileName);
                string[] lines;
                try
                {
                    if (!File.Exists(path)) continue;
                    lines = File.ReadAllLines(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var sourceLine in lines)
                {
                    var line = sourceLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;

                    var negated = line.StartsWith('!');
                    if (negated) line = line[1..].TrimStart();
                    if (line.Length == 0) continue;

                    additions.Add(new ScopedRule(
                        Normalize(baseRelativePath),
                        new GlobPattern(line),
                        negated));
                }
            }

            return additions.Count == 0
                ? this
                : new ScopedRules([.. rules, .. additions]);
        }

        public bool IsIgnored(string relativePath, bool isDirectory)
        {
            var normalized = Normalize(relativePath);
            var ignored = false;
            foreach (var rule in rules)
            {
                if (!IsWithin(normalized, rule.BaseRelativePath)) continue;
                var scopedPath = rule.BaseRelativePath.Length == 0
                    ? normalized
                    : normalized[(rule.BaseRelativePath.Length + 1)..];
                if (rule.Pattern.IsMatch(scopedPath, isDirectory))
                {
                    ignored = !rule.Negated;
                }
            }
            return ignored;
        }

        private static bool IsWithin(string path, string basePath) =>
            basePath.Length == 0
            || path.Equals(basePath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(basePath + '/', StringComparison.OrdinalIgnoreCase);

        private sealed record ScopedRule(string BaseRelativePath, GlobPattern Pattern, bool Negated);
    }

    private sealed class GlobPattern
    {
        private readonly Regex regex;
        private readonly bool matchAnySegment;
        private readonly bool directoryOnly;
        private readonly bool descendantTree;

        public GlobPattern(string source)
        {
            var pattern = Normalize(source);
            directoryOnly = pattern.EndsWith('/');
            pattern = pattern.TrimEnd('/');
            descendantTree = pattern.EndsWith("/**", StringComparison.Ordinal);
            if (descendantTree) pattern = pattern[..^3];
            var anchored = pattern.StartsWith('/');
            pattern = pattern.TrimStart('/');
            matchAnySegment = !anchored && !pattern.Contains('/');
            regex = new Regex(
                "^" + ToRegex(pattern) + (directoryOnly || descendantTree ? "(?:/.*)?" : "") + "$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        public bool IsMatch(string relativePath, bool isDirectory)
        {
            var normalized = Normalize(relativePath).Trim('/');
            if (directoryOnly && !isDirectory) return false;
            if (!matchAnySegment) return regex.IsMatch(normalized);
            return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => regex.IsMatch(segment));
        }

        private static string ToRegex(string pattern)
        {
            var builder = new System.Text.StringBuilder();
            for (var index = 0; index < pattern.Length; index++)
            {
                var current = pattern[index];
                if (current == '*')
                {
                    if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                    {
                        index++;
                        if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                        {
                            index++;
                            builder.Append("(?:.*/)?");
                        }
                        else
                        {
                            builder.Append(".*");
                        }
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }
                    continue;
                }

                if (current == '?')
                {
                    builder.Append("[^/]");
                    continue;
                }

                if (current == '[')
                {
                    var closing = pattern.IndexOf(']', index + 1);
                    if (closing > index + 1
                        && pattern.AsSpan(index + 1, closing - index - 1).ToArray().All(char.IsLetterOrDigit))
                    {
                        builder.Append(pattern.AsSpan(index, closing - index + 1));
                        index = closing;
                        continue;
                    }
                }

                builder.Append(Regex.Escape(current.ToString()));
            }
            return builder.ToString();
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim();
}
