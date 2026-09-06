using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MyTools.Common.Config;
using MyTools.Common;
using MyTools.Common.Plugins;

namespace MyTools.Plugins;

public sealed class SearchHistoryDbHelper
{
    private const string QueryHistoryTable = "search_query_history";
    private const string SelectionHistoryTable = "search_selection_history";
    private readonly string _dbPath;

    public SearchHistoryDbHelper(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(ConfigPath.DatabasePath, "search_history.db");
        Initialize();
    }

    public static string NormalizeQuery(string? query)
    {
        return query?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    public void RecordSearch(string? query)
    {
        var normalizedQuery = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return;
        }

        using var conn = CreateConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
INSERT INTO {QueryHistoryTable} (normalized_query, search_count, last_searched_at)
VALUES (@query, 1, @ts)
ON CONFLICT(normalized_query) DO UPDATE SET
    search_count = search_count + 1,
    last_searched_at = excluded.last_searched_at;";
        cmd.Parameters.AddWithValue("@query", normalizedQuery);
        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void RecordSelection(ResultItem item)
        => RecordSelection(item.SearchQuery, item.SourcePluginId, item.ResultKey, item.SearchFrom,
            snapshot: item.Args is SearchResultSnapshot ? null : SearchResultSnapshot.Create(item));

    public void UpdateSnapshot(ResultItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SourcePluginId) || string.IsNullOrWhiteSpace(item.ResultKey))
        {
            return;
        }

        using var conn = CreateConnection();
        conn.Open();
        using var save = conn.CreateCommand();
        save.CommandText = @"INSERT INTO search_result_snapshots (plugin_id, result_key, snapshot)
VALUES (@plugin, @key, @snapshot)
ON CONFLICT(plugin_id, result_key) DO UPDATE SET snapshot = excluded.snapshot;";
        save.Parameters.AddWithValue("@plugin", item.SourcePluginId);
        save.Parameters.AddWithValue("@key", item.ResultKey);
        save.Parameters.AddWithValue("@snapshot", JsonSerializer.Serialize(SearchResultSnapshot.Create(item)));
        save.ExecuteNonQuery();
    }

    public void RecordSelection(string? query, string pluginId, string resultKey,
        SearchFrom searchFrom = SearchFrom.Global, DateTime? selectedAt = null, SearchResultSnapshot? snapshot = null)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(resultKey))
        {
            return;
        }

        using var conn = CreateConnection();
        conn.Open();

        var timestamp = (selectedAt ?? DateTime.UtcNow).ToUniversalTime();
        using var transaction = conn.BeginTransaction();
        var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"
INSERT INTO {SelectionHistoryTable} (normalized_query, plugin_id, result_key, selected_count, last_selected_at)
VALUES (@query, @pluginId, @resultKey, 1, @ts)
ON CONFLICT(normalized_query, plugin_id, result_key) DO UPDATE SET
    selected_count = selected_count + 1,
    last_selected_at = excluded.last_selected_at;
INSERT INTO search_selection_daily (plugin_id, result_key, selection_day, selected_count, last_selected_at, query, search_from)
VALUES (@pluginId, @resultKey, @day, 1, @ts, @rawQuery, @searchFrom)
ON CONFLICT(plugin_id, result_key, selection_day) DO UPDATE SET
    selected_count = selected_count + 1,
    last_selected_at = excluded.last_selected_at,
    query = excluded.query,
    search_from = excluded.search_from;
DELETE FROM search_selection_daily WHERE selection_day < @oldestDay;";
        cmd.Parameters.AddWithValue("@query", NormalizeQuery(query));
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        cmd.Parameters.AddWithValue("@resultKey", resultKey);
        cmd.Parameters.AddWithValue("@ts", timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@day", timestamp.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@oldestDay", timestamp.Date.AddDays(-29).ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@rawQuery", query ?? string.Empty);
        cmd.Parameters.AddWithValue("@searchFrom", (int)searchFrom);
        cmd.ExecuteNonQuery();
        if (snapshot != null)
        {
            using var save = conn.CreateCommand();
            save.Transaction = transaction;
            save.CommandText = @"INSERT INTO search_result_snapshots (plugin_id, result_key, snapshot)
VALUES (@plugin, @key, @snapshot)
ON CONFLICT(plugin_id, result_key) DO UPDATE SET snapshot = excluded.snapshot;";
            save.Parameters.AddWithValue("@plugin", pluginId);
            save.Parameters.AddWithValue("@key", resultKey);
            save.Parameters.AddWithValue("@snapshot", JsonSerializer.Serialize(snapshot));
            save.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<SearchHistorySelection> GetRecentSelections(DateTime? now = null)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
WITH recent AS (
    SELECT *, SUM(selected_count) OVER (PARTITION BY plugin_id, result_key) AS frequency,
        ROW_NUMBER() OVER (PARTITION BY plugin_id, result_key ORDER BY last_selected_at DESC) AS position
    FROM search_selection_daily
    WHERE selection_day >= @oldestDay AND selection_day <= @today
)
SELECT recent.plugin_id, recent.result_key, query, search_from, frequency, last_selected_at, snapshot
FROM recent LEFT JOIN search_result_snapshots snapshots
ON recent.plugin_id = snapshots.plugin_id AND recent.result_key = snapshots.result_key
WHERE position = 1
ORDER BY last_selected_at DESC, recent.plugin_id, recent.result_key;";
        var today = (now ?? DateTime.UtcNow).ToUniversalTime().Date;
        cmd.Parameters.AddWithValue("@oldestDay", today.AddDays(-29).ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@today", today.ToString("yyyy-MM-dd"));
        var selections = new List<SearchHistorySelection>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            SearchResultSnapshot? snapshot = null;
            if (!reader.IsDBNull(6))
            {
                try { snapshot = JsonSerializer.Deserialize<SearchResultSnapshot>(reader.GetString(6)); }
                catch (JsonException) { /* Skip damaged presentation data. */ }
            }
            selections.Add(new SearchHistorySelection(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                (SearchFrom)reader.GetInt32(3), reader.GetInt64(4),
                DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind), snapshot));
        }
        return selections;
    }

    public void InvalidateSnapshot(string pluginId, string resultKey)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM search_result_snapshots WHERE plugin_id = @plugin AND result_key = @key";
        cmd.Parameters.AddWithValue("@plugin", pluginId);
        cmd.Parameters.AddWithValue("@key", resultKey);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, double> GetSelectionBoosts(string? query)
    {
        using var conn = CreateConnection();
        conn.Open();

        var normalizedQuery = NormalizeQuery(query);
        var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            cmd.CommandText = $@"
SELECT plugin_id, result_key, SUM(selected_count * 10.0) AS boost
FROM {SelectionHistoryTable}
GROUP BY plugin_id, result_key;";
        }
        else
        {
            cmd.CommandText = $@"
SELECT plugin_id,
       result_key,
       SUM(CASE WHEN normalized_query = @exactQuery THEN selected_count * 1000.0 ELSE 0 END)
            + SUM(CASE WHEN normalized_query LIKE @prefixQuery ESCAPE '\' AND normalized_query <> @exactQuery THEN selected_count * 200.0 ELSE 0 END)
       + SUM(selected_count * 10.0) AS boost
FROM {SelectionHistoryTable}
        WHERE normalized_query = @exactQuery OR normalized_query LIKE @prefixQuery ESCAPE '\'
GROUP BY plugin_id, result_key;";
            cmd.Parameters.AddWithValue("@exactQuery", normalizedQuery);
            cmd.Parameters.AddWithValue("@prefixQuery", EscapeLikeValue(normalizedQuery) + "%");
        }

        var boosts = new Dictionary<string, double>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var pluginId = reader.GetString(0);
            var resultKey = reader.GetString(1);
            var boost = reader.GetDouble(2);
            boosts[CombineKey(pluginId, resultKey)] = boost;
        }

        return boosts;
    }

    public static string CombineKey(string pluginId, string resultKey)
    {
        return pluginId + "::" + resultKey;
    }

    private void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        using var conn = CreateConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
CREATE TABLE IF NOT EXISTS {QueryHistoryTable} (
    normalized_query TEXT PRIMARY KEY,
    search_count INTEGER NOT NULL,
    last_searched_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS {SelectionHistoryTable} (
    normalized_query TEXT NOT NULL,
    plugin_id TEXT NOT NULL,
    result_key TEXT NOT NULL,
    selected_count INTEGER NOT NULL,
    last_selected_at TEXT NOT NULL,
    PRIMARY KEY (normalized_query, plugin_id, result_key)
);

CREATE INDEX IF NOT EXISTS idx_selection_plugin_result
ON {SelectionHistoryTable} (plugin_id, result_key);

CREATE INDEX IF NOT EXISTS idx_selection_query
ON {SelectionHistoryTable} (normalized_query);

CREATE TABLE IF NOT EXISTS search_selection_daily (
    plugin_id TEXT NOT NULL,
    result_key TEXT NOT NULL,
    selection_day TEXT NOT NULL,
    selected_count INTEGER NOT NULL,
    last_selected_at TEXT NOT NULL,
    query TEXT NOT NULL,
    search_from INTEGER NOT NULL,
    PRIMARY KEY (plugin_id, result_key, selection_day)
);

CREATE TABLE IF NOT EXISTS search_result_snapshots (
    plugin_id TEXT NOT NULL,
    result_key TEXT NOT NULL,
    snapshot TEXT NOT NULL,
    PRIMARY KEY (plugin_id, result_key)
);

-- Legacy history contains only lifetime totals. Import its last known selection.
INSERT OR IGNORE INTO search_selection_daily
    (plugin_id, result_key, selection_day, selected_count, last_selected_at, query, search_from)
SELECT plugin_id, result_key, substr(MAX(last_selected_at), 1, 10), 1,
    MAX(last_selected_at), normalized_query, 0
FROM {SelectionHistoryTable}
WHERE last_selected_at >= @oldestDay
GROUP BY plugin_id, result_key;";
        cmd.Parameters.AddWithValue("@oldestDay", DateTime.UtcNow.Date.AddDays(-29).ToString("yyyy-MM-dd"));
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_dbPath};Pooling=False");
    }

    private static string EscapeLikeValue(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
