using Microsoft.Data.Sqlite;
using SnCollector.Core.Models;

namespace SnCollector.Core.Storage;

/// <summary>
/// 历史数据 SQLite 存储：%AppData%/SnCollector/history.db。
/// Records（主表）+ RecordValues（明细表），天然支持任意多个数据项扩展。
/// </summary>
public sealed class RecordStore : IDisposable
{
    private readonly string _dbPath;
    private bool _disposed;

    public RecordStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SnCollector");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "history.db");
        Initialize();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Records(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                IsOk INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS RecordValues(
                RecordId INTEGER NOT NULL REFERENCES Records(Id),
                ItemName TEXT NOT NULL,
                Value TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_records_time ON Records(Timestamp);
            CREATE INDEX IF NOT EXISTS idx_values_record ON RecordValues(RecordId);
            CREATE INDEX IF NOT EXISTS idx_values_name ON RecordValues(ItemName);
            """;
        cmd.ExecuteNonQuery();
    }

    public long Insert(SnRecord record)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        long id;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Records(Timestamp, IsOk) VALUES ($ts, $ok); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$ts", record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            cmd.Parameters.AddWithValue("$ok", record.IsOk ? 1 : 0);
            id = (long)cmd.ExecuteScalar()!;
        }
        foreach (var kv in record.Values)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RecordValues(RecordId, ItemName, Value) VALUES ($rid, $name, $val)";
            cmd.Parameters.AddWithValue("$rid", id);
            cmd.Parameters.AddWithValue("$name", kv.Key);
            cmd.Parameters.AddWithValue("$val", kv.Value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    /// <summary>按时间范围 + 关键字 + 结果查询历史</summary>
    public List<SnRecord> Query(DateTime from, DateTime to, string? keyword, bool? isOk, int limit = 500)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT r.Id, r.Timestamp, r.IsOk, v.ItemName, v.Value
            FROM Records r LEFT JOIN RecordValues v ON v.RecordId = r.Id
            WHERE r.Timestamp >= $from AND r.Timestamp <= $to
            """;
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd HH:mm:ss"));
        if (isOk.HasValue)
        {
            sql += " AND r.IsOk = $ok";
            cmd.Parameters.AddWithValue("$ok", isOk.Value ? 1 : 0);
        }
        sql += " ORDER BY r.Id DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.CommandText = sql;

        var map = new Dictionary<long, SnRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            if (!map.TryGetValue(id, out var rec))
            {
                rec = new SnRecord
                {
                    Id = id,
                    Timestamp = DateTime.Parse(reader.GetString(1)),
                    IsOk = reader.GetInt32(2) == 1,
                };
                map[id] = rec;
            }
            if (!reader.IsDBNull(3))
                rec.Values[reader.GetString(3)] = reader.GetString(4);
        }
        var list = map.Values.ToList();
        if (!string.IsNullOrWhiteSpace(keyword))
            list = list.Where(r => r.Values.Values.Any(v => v.Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();
        return list;
    }

    public int CountToday()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Records WHERE date(Timestamp) = date('now', 'localtime')";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int CountAll()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Records";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
