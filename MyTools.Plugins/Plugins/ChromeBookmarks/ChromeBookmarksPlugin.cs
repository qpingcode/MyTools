using System.IO;
using Newtonsoft.Json.Linq;
using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Common.Utils;
using MyTools.Plugins.Param;

namespace MyTools.Plugins;

public sealed record ChromeBookmark(string Title, string Url, string FolderPath);

public class ChromeBookmarkReader
{
    public virtual IReadOnlyList<ChromeBookmark> ReadBookmarks(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        var json = File.ReadAllText(filePath);
        var root = JObject.Parse(json);
        var roots = root["roots"] as JObject;
        if (roots == null)
        {
            return [];
        }

        var results = new List<ChromeBookmark>();
        foreach (var property in roots.Properties())
        {
            var rootLabel = GetRootLabel(property.Name);
            CollectBookmarks(property.Value["children"], rootLabel, results);
        }

        return results;
    }

    private static void CollectBookmarks(JToken? node, string currentPath, ICollection<ChromeBookmark> results)
    {
        if (node is not JArray children)
        {
            return;
        }

        foreach (var child in children)
        {
            var type = child["type"]?.Value<string>();
            if (string.Equals(type, "url", StringComparison.OrdinalIgnoreCase))
            {
                var title = child["name"]?.Value<string>();
                var url = child["url"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                {
                    results.Add(new ChromeBookmark(title, url, currentPath));
                }
                continue;
            }

            if (string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase))
            {
                var name = child["name"]?.Value<string>();
                var nextPath = string.IsNullOrWhiteSpace(name) ? currentPath : $"{currentPath}/{name}";
                CollectBookmarks(child["children"], nextPath, results);
            }
        }
    }

    private static string GetRootLabel(string rootName) => rootName switch
    {
        "bookmark_bar" => "Bar",
        "other" => "Other",
        "synced" => "Mobile",
        _ => rootName,
    };
}

public class ChromeBookmarksPlugin : PluginBase
{
    private readonly string? _bookmarkFilePathOverride;
    private readonly ChromeBookmarkReader _bookmarkReader;
    private readonly Icon _icon = new StringIcon("🔖");
    private List<ChromeBookmark> _bookmarks = [];
    private string? _resolvedBookmarkFilePath;
    private DateTime _bookmarksLastWriteTimeUtc;

    public ChromeBookmarksPlugin() : this(null, null)
    {
    }

    public ChromeBookmarksPlugin(string? bookmarkFilePath, ChromeBookmarkReader? bookmarkReader)
    {
        _bookmarkFilePathOverride = bookmarkFilePath;
        _bookmarkReader = bookmarkReader ?? new ChromeBookmarkReader();
    }

    public override string Name => "Chrome Bookmarks";
    public override string Description => "Search local Chrome bookmarks and open them in browser";
    public override List<IActionWithCommand> Actions => [WellKnownActions.OpenInBrowser.WithDefaultCommand()];
    public override bool IsGlobalSearchPlugin => true;

    public override Task InitializeAsync()
    {
        _resolvedBookmarkFilePath = _bookmarkFilePathOverride ?? GetDefaultBookmarksFilePath();
        ReloadBookmarks(force: true);
        return Task.CompletedTask;
    }

    public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        ReloadBookmarks();

        if (string.IsNullOrWhiteSpace(query) || _bookmarks.Count == 0)
        {
            return Task.FromResult(Result.CreateEmpty());
        }

        var items = _bookmarks
            .Where(bookmark => IsMatch(bookmark, query))
            .Select(bookmark => new ResultItem(
                _icon,
                bookmark.Title,
                string.IsNullOrWhiteSpace(bookmark.FolderPath) ? bookmark.Url : $"{bookmark.FolderPath} - {bookmark.Url}",
                ActionStringParam.From(bookmark.Url),
                ResultItemPriorities.Medium));

        return Task.FromResult(Result.CreateSuccessResult(items));
    }

    private void ReloadBookmarks(bool force = false)
    {
        if (string.IsNullOrWhiteSpace(_resolvedBookmarkFilePath))
        {
            _bookmarks = [];
            pluginState.IsEnabled = false;
            return;
        }

        if (!File.Exists(_resolvedBookmarkFilePath))
        {
            _bookmarks = [];
            _bookmarksLastWriteTimeUtc = DateTime.MinValue;
            pluginState.IsEnabled = false;
            return;
        }

        var lastWriteTimeUtc = File.GetLastWriteTimeUtc(_resolvedBookmarkFilePath);
        if (!force && lastWriteTimeUtc <= _bookmarksLastWriteTimeUtc)
        {
            return;
        }

        _bookmarks = _bookmarkReader.ReadBookmarks(_resolvedBookmarkFilePath).ToList();
        _bookmarksLastWriteTimeUtc = lastWriteTimeUtc;
        pluginState.IsEnabled = _bookmarks.Count > 0;
    }

    private static bool IsMatch(ChromeBookmark bookmark, string query)
    {
        if (bookmark.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            bookmark.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            bookmark.FolderPath.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return StringUtils.IsSubsequence(query.ToLowerInvariant(), bookmark.Title.ToLowerInvariant());
    }

    private static string GetDefaultBookmarksFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google",
            "Chrome",
            "User Data",
            "Default",
            "Bookmarks");
    }
}
