using System.IO;
using Microsoft.Data.Sqlite;
using MyTools.Common.Config;

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

    public void RecordSelection(string? query, string pluginId, string resultKey)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(resultKey))
        {
            return;
        }

        using var conn = CreateConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
INSERT INTO {SelectionHistoryTable} (normalized_query, plugin_id, result_key, selected_count, last_selected_at)
VALUES (@query, @pluginId, @resultKey, 1, @ts)
ON CONFLICT(normalized_query, plugin_id, result_key) DO UPDATE SET
    selected_count = selected_count + 1,
    last_selected_at = excluded.last_selected_at;";
        cmd.Parameters.AddWithValue("@query", NormalizeQuery(query));
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        cmd.Parameters.AddWithValue("@resultKey", resultKey);
        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
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
ON {SelectionHistoryTable} (normalized_query);";
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