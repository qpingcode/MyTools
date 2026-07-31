using System.IO;
using MyTools.Common;
using SqliteConnection = Microsoft.Data.Sqlite.SqliteConnection;

namespace MyTools.Plugins;

public class ClipBoardHistoryItem
{
    public int Id { get; set; }
    public string Summary { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public DateTime Timestamp { get; set; }
}

public class ClipBoardDbHelper
{
    private readonly string _dbPath;
    private const int CleanRecentHistoryReservedCount = 50;
    private const int CleanRecentHistoryBeforeDays = 3;
    private ClipBoardHistoryItem? last;
    
    public ClipBoardDbHelper(string dbPath)
    {
        _dbPath = dbPath;
        Initialize();
    }

    private void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS clipboard_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            content BLOB NOT NULL,
            summary TEXT NOT NULL,
            hash TEXT NOT NULL,
            timestamp TEXT NOT NULL
        );";
        cmd.ExecuteNonQuery();
    }
    
    public void AddHistory(byte[] content, string summary, string hash)
    {
        
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        if (IsDuplicate(conn, hash))
        {
            UpdateLastHistory(conn, hash);
            return;
        }
        
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO clipboard_history (content, summary,timestamp, hash) VALUES (@content, @summary, @ts, @hash)";
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@summary", summary);
        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.ExecuteNonQuery();

        last = new ClipBoardHistoryItem
        {
            Content = content
        };
    }

    private void UpdateLastHistory(SqliteConnection conn, string hash)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE clipboard_history SET timestamp = @ts WHERE hash = @hash";
        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.ExecuteNonQuery();
    }

    private bool IsDuplicate(SqliteConnection connection, string hash)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(1) FROM clipboard_history WHERE hash = @hash";
        cmd.Parameters.AddWithValue("@hash", hash);
        var count = cmd.ExecuteScalar();
        long countValue = Convert.ToInt64(count); 
        if (countValue > 0)
        {
            return true;
        }
        return false;
    }

    private string GetSelectFields(bool includeContent)
    {
        return includeContent 
            ? "id, content, summary, timestamp" 
            : "id, summary, timestamp";
    }

    private string BuildWhereClause(string? query, out bool hasQuery)
    {
        hasQuery = !string.IsNullOrEmpty(query);
        return hasQuery ? "WHERE summary LIKE @q" : string.Empty;
    }

    public List<ClipBoardHistoryItem> Search(string? query, int max = 50, bool includeContent = true)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        
        var fields = GetSelectFields(includeContent);
        var whereClause = BuildWhereClause(query, out var hasQuery);
        cmd.CommandText = $"SELECT {fields} FROM clipboard_history {whereClause} ORDER BY timestamp DESC LIMIT @max";
        
        if (hasQuery)
        {
            cmd.Parameters.AddWithValue("@q", "%" + query + "%");
        }
        cmd.Parameters.AddWithValue("@max", max);
        
        var list = new List<ClipBoardHistoryItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(reader.GetOrdinal("id"));
            var summary = reader.GetString(reader.GetOrdinal("summary"));
            var timestamp = reader.GetString(reader.GetOrdinal("timestamp"));
            
            var item = new ClipBoardHistoryItem
            {
                Id = id,
                Content = includeContent ? (byte[])reader["content"] : [],
                Summary = summary,
                Timestamp = DateTime.Parse(timestamp)
            };
            list.Add(item);
        }
        return list;
    }

    public byte[]? GetContentById(int id)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT content FROM clipboard_history WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var result = cmd.ExecuteScalar();
        return result as byte[];
    }

    public void CleanupOldHistory()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // 1. 获取最近RecentHistoryCount条的id
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT id FROM clipboard_history ORDER BY timestamp DESC LIMIT {CleanRecentHistoryReservedCount}";
        var recentIds = new HashSet<int>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                recentIds.Add(reader.GetInt32(0));
            }
        }
        // 2. 删除3天前且不在recentIds中的历史
        var threeDaysAgo = DateTime.UtcNow.AddDays(-1 * CleanRecentHistoryBeforeDays).ToString("o");
        var idList = string.Join(",", recentIds);
        var delCmd = conn.CreateCommand();
        if (recentIds.Count > 0)
        {
            delCmd.CommandText = $@"DELETE FROM clipboard_history WHERE timestamp < @ts AND id NOT IN ({idList})";
        }
        else
        {
            delCmd.CommandText = $@"DELETE FROM clipboard_history WHERE timestamp < @ts";
        }
        delCmd.Parameters.AddWithValue("@ts", threeDaysAgo);
        delCmd.ExecuteNonQuery();
    }
} 