using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.Core.Services;

/// <summary>
/// Local SQLite persistence under LocalAppData (<c>chronos.db</c>).
/// Global laps use <c>contest_id IS NULL</c>; contest laps use the contest Guid.
/// </summary>
public sealed class LocalChronosDb : IDisposable
{
    public const string FileName = "chronos.db";
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SqliteConnection _conn;
    private readonly object _gate = new();
    private bool _disposed;

    public string DatabasePath { get; }
    public string DataDirectory { get; }

    private LocalChronosDb(string dataDirectory, SqliteConnection conn)
    {
        DataDirectory = dataDirectory;
        DatabasePath = Path.Combine(dataDirectory, FileName);
        _conn = conn;
    }

    public static LocalChronosDb Open(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FileName);
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        conn.Open();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        var db = new LocalChronosDb(dataDirectory, conn);
        db.EnsureSchema();
        return db;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _conn.Dispose();
    }

    public bool IsJsonMigrationCompleted()
    {
        lock (_gate)
            return string.Equals(GetMeta("json_migrated"), "1", StringComparison.Ordinal);
    }

    public void MarkJsonMigrationCompleted()
    {
        lock (_gate)
            SetMeta("json_migrated", "1");
    }

    public IReadOnlyList<Contest> LoadContests()
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT id, name, status, track_filter, created_at, started_at, stopped_at
                FROM contests
                ORDER BY created_at DESC
                """;
            var list = new List<Contest>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Contest
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Status = ParseStatus(reader.GetString(2)),
                    TrackFilter = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    CreatedAt = ParseUtc(reader.GetString(4)),
                    StartedAt = reader.IsDBNull(5) ? null : ParseUtc(reader.GetString(5)),
                    StoppedAt = reader.IsDBNull(6) ? null : ParseUtc(reader.GetString(6)),
                });
            }

            return list;
        }
    }

    /// <summary>
    /// Upserts the given contests and removes any contest (and its laps) not in the list.
    /// </summary>
    public void ReplaceContests(IReadOnlyList<Contest> contests)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            var keep = contests.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

            var existingIds = new List<string>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id FROM contests";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    existingIds.Add(reader.GetString(0));
            }

            foreach (var id in existingIds)
            {
                if (keep.Contains(id))
                    continue;

                using (var laps = _conn.CreateCommand())
                {
                    laps.Transaction = tx;
                    laps.CommandText = "DELETE FROM laps WHERE contest_id = $id";
                    laps.Parameters.AddWithValue("$id", id);
                    laps.ExecuteNonQuery();
                }

                using (var del = _conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM contests WHERE id = $id";
                    del.Parameters.AddWithValue("$id", id);
                    del.ExecuteNonQuery();
                }
            }

            foreach (var c in contests)
                UpsertContestLocked(tx, c);

            tx.Commit();
        }
    }

    public void UpsertContest(Contest contest)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            UpsertContestLocked(tx, contest);
            tx.Commit();
        }
    }

    public void DeleteContest(string contestId)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            using (var laps = _conn.CreateCommand())
            {
                laps.Transaction = tx;
                laps.CommandText = "DELETE FROM laps WHERE contest_id = $id";
                laps.Parameters.AddWithValue("$id", contestId);
                laps.ExecuteNonQuery();
            }

            using (var contests = _conn.CreateCommand())
            {
                contests.Transaction = tx;
                contests.CommandText = "DELETE FROM contests WHERE id = $id";
                contests.Parameters.AddWithValue("$id", contestId);
                contests.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    /// <summary>Load all non-deleted laps for global (<paramref name="contestId"/> null) or a contest.</summary>
    public Dictionary<int, List<ChronoEntry>> LoadLapsByTrack(string? contestId)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            if (contestId is null)
            {
                cmd.CommandText =
                    """
                    SELECT id, track_id, track_name, name, best_lap_ms, started_at, ended_at, custom_setup
                    FROM laps
                    WHERE contest_id IS NULL AND deleted_at IS NULL
                    """;
            }
            else
            {
                cmd.CommandText =
                    """
                    SELECT id, track_id, track_name, name, best_lap_ms, started_at, ended_at, custom_setup
                    FROM laps
                    WHERE contest_id = $cid AND deleted_at IS NULL
                    """;
                cmd.Parameters.AddWithValue("$cid", contestId);
            }

            var byTrack = new Dictionary<int, List<ChronoEntry>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var entry = new ChronoEntry
                {
                    Id = reader.GetString(0),
                    TrackId = reader.GetInt32(1),
                    TrackName = reader.GetString(2),
                    Name = reader.GetString(3),
                    BestLapMs = (uint)reader.GetInt64(4),
                    StartedAt = ParseUtc(reader.GetString(5)),
                    EndedAt = reader.IsDBNull(6) ? null : ParseUtc(reader.GetString(6)),
                    CustomSetup = reader.GetInt32(7) != 0,
                };

                if (!byTrack.TryGetValue(entry.TrackId, out var list))
                {
                    list = [];
                    byTrack[entry.TrackId] = list;
                }

                list.Add(entry);
            }

            return byTrack;
        }
    }

    /// <summary>
    /// Replace all laps for one track in a scope (full snapshot after dirty flush).
    /// Empty <paramref name="entries"/> deletes the track's rows.
    /// </summary>
    public void ReplaceTrackLaps(string? contestId, int trackId, IReadOnlyList<ChronoEntry> entries)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            using (var del = _conn.CreateCommand())
            {
                del.Transaction = tx;
                if (contestId is null)
                {
                    del.CommandText = "DELETE FROM laps WHERE contest_id IS NULL AND track_id = $tid";
                }
                else
                {
                    del.CommandText = "DELETE FROM laps WHERE contest_id = $cid AND track_id = $tid";
                    del.Parameters.AddWithValue("$cid", contestId);
                }

                del.Parameters.AddWithValue("$tid", trackId);
                del.ExecuteNonQuery();
            }

            foreach (var e in entries)
            {
                if (e.BestLapMs is not > 0 || e.TrackId < 0)
                    continue;

                using var ins = _conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText =
                    """
                    INSERT INTO laps (
                      id, contest_id, track_id, track_name, name, best_lap_ms,
                      started_at, ended_at, custom_setup, deleted_at
                    ) VALUES (
                      $id, $cid, $tid, $tname, $name, $ms, $started, $ended, $custom, NULL
                    )
                    """;
                ins.Parameters.AddWithValue("$id", e.Id);
                ins.Parameters.AddWithValue("$cid", (object?)contestId ?? DBNull.Value);
                ins.Parameters.AddWithValue("$tid", e.TrackId);
                ins.Parameters.AddWithValue("$tname", e.TrackName ?? "");
                ins.Parameters.AddWithValue("$name", e.Name ?? "");
                ins.Parameters.AddWithValue("$ms", (long)e.BestLapMs.Value);
                ins.Parameters.AddWithValue("$started", FormatUtc(e.StartedAt));
                ins.Parameters.AddWithValue("$ended", e.EndedAt is DateTime end ? FormatUtc(end) : DBNull.Value);
                ins.Parameters.AddWithValue("$custom", e.CustomSetup ? 1 : 0);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public void DeleteAllLaps(string? contestId)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            if (contestId is null)
                cmd.CommandText = "DELETE FROM laps WHERE contest_id IS NULL";
            else
            {
                cmd.CommandText = "DELETE FROM laps WHERE contest_id = $cid";
                cmd.Parameters.AddWithValue("$cid", contestId);
            }

            cmd.ExecuteNonQuery();
        }
    }

    public int CountLaps(string? contestId = null)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            if (contestId is null)
                cmd.CommandText = "SELECT COUNT(*) FROM laps WHERE deleted_at IS NULL";
            else
            {
                cmd.CommandText = "SELECT COUNT(*) FROM laps WHERE contest_id = $cid AND deleted_at IS NULL";
                cmd.Parameters.AddWithValue("$cid", contestId);
            }

            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    private void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS meta (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS contests (
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              status TEXT NOT NULL,
              track_filter INTEGER,
              created_at TEXT NOT NULL,
              started_at TEXT,
              stopped_at TEXT
            );

            CREATE TABLE IF NOT EXISTS laps (
              id TEXT NOT NULL,
              contest_id TEXT,
              track_id INTEGER NOT NULL,
              track_name TEXT NOT NULL,
              name TEXT NOT NULL,
              best_lap_ms INTEGER NOT NULL,
              started_at TEXT NOT NULL,
              ended_at TEXT,
              custom_setup INTEGER NOT NULL DEFAULT 0,
              deleted_at TEXT,
              PRIMARY KEY (id)
            );

            CREATE INDEX IF NOT EXISTS idx_laps_scope_track
              ON laps (contest_id, track_id, best_lap_ms);
            CREATE INDEX IF NOT EXISTS idx_laps_started
              ON laps (started_at DESC);
            """;
        cmd.ExecuteNonQuery();

        if (GetMeta("schema_version") is null)
            SetMeta("schema_version", SchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    private string? GetMeta(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetMeta(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO meta (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private void UpsertContestLocked(SqliteTransaction tx, Contest contest)
    {
        using var ins = _conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText =
            """
            INSERT INTO contests (id, name, status, track_filter, created_at, started_at, stopped_at)
            VALUES ($id, $name, $status, $tf, $created, $started, $stopped)
            ON CONFLICT(id) DO UPDATE SET
              name = excluded.name,
              status = excluded.status,
              track_filter = excluded.track_filter,
              created_at = excluded.created_at,
              started_at = excluded.started_at,
              stopped_at = excluded.stopped_at
            """;
        ins.Parameters.AddWithValue("$id", contest.Id);
        ins.Parameters.AddWithValue("$name", contest.Name);
        ins.Parameters.AddWithValue("$status", contest.Status.ToString().ToLowerInvariant());
        ins.Parameters.AddWithValue("$tf", (object?)contest.TrackFilter ?? DBNull.Value);
        ins.Parameters.AddWithValue("$created", FormatUtc(contest.CreatedAt));
        ins.Parameters.AddWithValue("$started", contest.StartedAt is DateTime s ? FormatUtc(s) : DBNull.Value);
        ins.Parameters.AddWithValue("$stopped", contest.StoppedAt is DateTime t ? FormatUtc(t) : DBNull.Value);
        ins.ExecuteNonQuery();
    }

    private static ContestStatus ParseStatus(string raw) =>
        raw.ToLowerInvariant() switch
        {
            "active" => ContestStatus.Active,
            "stopped" => ContestStatus.Stopped,
            _ => ContestStatus.Draft,
        };

    private static string FormatUtc(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseUtc(string raw) =>
        DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    /// <summary>JSON options shared with migrator for contest index.</summary>
    internal static JsonSerializerOptions SharedJsonOptions => JsonOptions;
}
