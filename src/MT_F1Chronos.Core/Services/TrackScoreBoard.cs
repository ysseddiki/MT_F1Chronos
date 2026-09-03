using System.Text.Json;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.Core.Services;

/// <summary>
/// In-memory per-track chrono board with deferred dirty tracking.
/// Persistence directory is supplied by the owner (global sessions or a contest folder).
/// </summary>
public sealed class TrackScoreBoard
{
    public const int MaxEntriesPerTrack = 5_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly TimeProvider _time;
    private readonly Dictionary<int, List<ChronoEntry>> _byTrack = new();
    private readonly HashSet<int> _dirty = new();
    private readonly HashSet<string> _deletedIds = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public event Action? BecameDirty;

    public TrackScoreBoard(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public void LoadFromDirectory(string directory)
    {
        Directory.CreateDirectory(directory);

        lock (_gate)
        {
            _byTrack.Clear();
            _dirty.Clear();
            _deletedIds.Clear();

            if (!Directory.Exists(directory))
                return;

            foreach (var path in Directory.EnumerateFiles(directory, "track-*.json"))
            {
                if (!TryParseTrackId(path, out var trackId))
                    continue;

                var entries = CapEntries(ReadTrackFile(path));
                if (entries.Count == 0)
                    continue;

                _byTrack[trackId] = entries;
            }
        }
    }

    public void Record(string playerName, int trackId, string trackName, uint lapMs)
    {
        if (string.IsNullOrWhiteSpace(playerName) || trackId < 0 || lapMs == 0)
            return;

        lock (_gate)
        {
            if (!_byTrack.TryGetValue(trackId, out var list))
            {
                list = [];
                _byTrack[trackId] = list;
            }

            var now = _time.GetUtcNow().UtcDateTime;
            list.Add(new ChronoEntry
            {
                Name = playerName.Trim(),
                TrackId = trackId,
                TrackName = trackName,
                BestLapMs = lapMs,
                StartedAt = now,
                EndedAt = now,
            });

            if (list.Count > MaxEntriesPerTrack)
            {
                var capped = CapEntries(list);
                list.Clear();
                list.AddRange(capped);
            }

            MarkDirtyLocked(trackId);
        }

        BecameDirty?.Invoke();
    }

    public IReadOnlyList<LeaderboardRow> GetLeaderboard(
        int trackId,
        int count = LeaderboardSizes.Default,
        bool bestPerPlayer = false) =>
        RankedLaps(trackId, bestPerPlayer).Take(LeaderboardSizes.Normalize(count)).ToList();

    public IReadOnlyList<TrackSummary> GetTracksWithScores()
    {
        lock (_gate)
        {
            return _byTrack
                .Select(kv =>
                {
                    var scored = kv.Value.Where(s => s.BestLapMs is > 0).ToList();
                    if (scored.Count == 0)
                        return null;

                    var latest = scored.OrderByDescending(s => s.StartedAt).First();
                    return new TrackSummary
                    {
                        TrackId = kv.Key,
                        TrackName = latest.TrackName,
                        ScoreCount = scored.Count,
                    };
                })
                .Where(t => t is not null)
                .Select(t => t!)
                .OrderBy(t => t.TrackName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<LeaderboardRow> GetScoresForTrack(
        int trackId,
        bool bestPerPlayer = false,
        string? playerName = null) =>
        RankedLaps(trackId, bestPerPlayer, playerName).ToList();

    public IReadOnlyList<string> GetPlayerNamesForTrack(int trackId)
    {
        lock (_gate)
        {
            if (!_byTrack.TryGetValue(trackId, out var list))
                return [];

            return list
                .Where(s => s.BestLapMs is > 0 && !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => s.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool DeleteEntry(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return false;

        int? dirtyTrack = null;
        lock (_gate)
        {
            foreach (var (trackId, list) in _byTrack)
            {
                var removed = list.RemoveAll(s => string.Equals(s.Id, entryId, StringComparison.Ordinal));
                if (removed > 0)
                {
                    _deletedIds.Add(entryId);
                    dirtyTrack = trackId;
                    MarkDirtyLocked(trackId);
                    break;
                }
            }
        }

        if (dirtyTrack is null)
            return false;

        BecameDirty?.Invoke();
        return true;
    }

    public int DeletePlayerOnTrack(string playerName, int trackId)
    {
        if (string.IsNullOrWhiteSpace(playerName) || trackId < 0)
            return 0;

        int removed;
        lock (_gate)
        {
            if (!_byTrack.TryGetValue(trackId, out var list))
                return 0;

            var removedIds = list
                .Where(s => string.Equals(s.Name, playerName.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Id)
                .ToList();
            removed = list.RemoveAll(s =>
                string.Equals(s.Name, playerName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                foreach (var id in removedIds)
                    _deletedIds.Add(id);
                MarkDirtyLocked(trackId);
            }
        }

        if (removed > 0)
            BecameDirty?.Invoke();

        return removed;
    }

    public int ClearTrack(int trackId)
    {
        if (trackId < 0)
            return 0;

        int removed;
        lock (_gate)
        {
            if (!_byTrack.TryGetValue(trackId, out var list))
                return 0;

            removed = list.Count;
            foreach (var entry in list)
                _deletedIds.Add(entry.Id);
            _byTrack.Remove(trackId);
            MarkDirtyLocked(trackId);
        }

        if (removed > 0)
            BecameDirty?.Invoke();

        return removed;
    }

    public int ClearAll()
    {
        int removed;
        List<int> trackIds;
        lock (_gate)
        {
            removed = _byTrack.Values.Sum(list => list.Count);
            if (removed == 0)
                return 0;

            trackIds = _byTrack.Keys.ToList();
            foreach (var list in _byTrack.Values)
            {
                foreach (var entry in list)
                    _deletedIds.Add(entry.Id);
            }
            _byTrack.Clear();
            foreach (var trackId in trackIds)
                _dirty.Add(trackId);
        }

        BecameDirty?.Invoke();
        return removed;
    }

    public bool RenameEntry(string entryId, string newName)
    {
        var trimmed = ClampName(newName);
        if (string.IsNullOrWhiteSpace(entryId) || string.IsNullOrWhiteSpace(trimmed))
            return false;

        lock (_gate)
        {
            foreach (var (trackId, list) in _byTrack)
            {
                var entry = list.FirstOrDefault(s => string.Equals(s.Id, entryId, StringComparison.Ordinal));
                if (entry is null)
                    continue;

                entry.Name = trimmed;
                MarkDirtyLocked(trackId);
                BecameDirty?.Invoke();
                return true;
            }
        }

        return false;
    }

    public int RenamePlayer(string oldName, string newName)
    {
        var from = oldName?.Trim() ?? string.Empty;
        var to = ClampName(newName);
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return 0;

        var renamed = 0;
        lock (_gate)
        {
            foreach (var (trackId, list) in _byTrack)
            {
                var hit = false;
                foreach (var entry in list)
                {
                    if (!string.Equals(entry.Name, from, StringComparison.OrdinalIgnoreCase))
                        continue;
                    entry.Name = to;
                    renamed++;
                    hit = true;
                }

                if (hit)
                    MarkDirtyLocked(trackId);
            }
        }

        if (renamed > 0)
            BecameDirty?.Invoke();
        return renamed;
    }

    public bool RestoreEntry(ChronoEntry entry)
    {
        if (entry.TrackId < 0 || string.IsNullOrWhiteSpace(entry.Id) || entry.BestLapMs is not > 0)
            return false;

        lock (_gate)
        {
            if (!_byTrack.TryGetValue(entry.TrackId, out var list))
            {
                list = [];
                _byTrack[entry.TrackId] = list;
            }

            if (list.Any(s => string.Equals(s.Id, entry.Id, StringComparison.Ordinal)))
                return true;

            list.Add(new ChronoEntry
            {
                Id = entry.Id,
                Name = ClampName(entry.Name),
                TrackId = entry.TrackId,
                TrackName = entry.TrackName,
                BestLapMs = entry.BestLapMs,
                StartedAt = entry.StartedAt,
                EndedAt = entry.EndedAt ?? entry.StartedAt,
            });
            _deletedIds.Remove(entry.Id);
            MarkDirtyLocked(entry.TrackId);
        }

        BecameDirty?.Invoke();
        return true;
    }

    public IReadOnlyList<string> PeekDeletedIds()
    {
        lock (_gate)
            return _deletedIds.ToList();
    }

    public void AcknowledgeDeletedIds(IEnumerable<string> ids)
    {
        lock (_gate)
        {
            foreach (var id in ids)
                _deletedIds.Remove(id);
        }
    }

    private static string ClampName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return trimmed.Length <= ResultsSyncProtocol.MaxPlayerNameLength
            ? trimmed
            : trimmed[..ResultsSyncProtocol.MaxPlayerNameLength];
    }

    public IReadOnlyList<ChronoEntry> GetAllScoredEntries()
    {
        lock (_gate)
        {
            return _byTrack.Values
                .SelectMany(list => list)
                .Where(s => s.BestLapMs is > 0)
                .OrderBy(s => s.TrackName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.BestLapMs)
                .ThenBy(s => s.StartedAt)
                .ToList();
        }
    }

    public IReadOnlyList<string> GetRecentPlayerNames(int max = 10)
    {
        lock (_gate)
        {
            return _byTrack.Values
                .SelectMany(list => list)
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .OrderByDescending(s => s.StartedAt)
                .Select(s => s.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .ToList();
        }
    }

    public int EntryCount
    {
        get
        {
            lock (_gate)
                return _byTrack.Values.Sum(list => list.Count(s => s.BestLapMs is > 0));
        }
    }

    public int MostRecentScoredTrackId()
    {
        lock (_gate)
        {
            return _byTrack.Values
                .SelectMany(list => list)
                .Where(s => s.BestLapMs is > 0)
                .OrderByDescending(s => s.StartedAt)
                .Select(s => (int?)s.TrackId)
                .FirstOrDefault() ?? -1;
        }
    }

    public string? GetStoredTrackName(int trackId)
    {
        lock (_gate)
        {
            if (!_byTrack.TryGetValue(trackId, out var list))
                return null;

            return list
                .Where(s => !string.IsNullOrWhiteSpace(s.TrackName))
                .OrderByDescending(s => s.StartedAt)
                .Select(s => s.TrackName)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Drains dirty tracks and returns snapshots to persist.
    /// Empty list means the track file should be deleted.
    /// </summary>
    public Dictionary<int, List<ChronoEntry>> DrainDirty()
    {
        lock (_gate)
        {
            if (_dirty.Count == 0)
                return new Dictionary<int, List<ChronoEntry>>();

            var result = _dirty.ToDictionary(
                trackId => trackId,
                trackId => _byTrack.TryGetValue(trackId, out var list)
                    ? list.ToList()
                    : []);
            _dirty.Clear();
            return result;
        }
    }

    public bool HasDirty
    {
        get
        {
            lock (_gate)
                return _dirty.Count > 0;
        }
    }

    public void PersistDirty(string directory)
    {
        var dirty = DrainDirty();
        if (dirty.Count == 0)
            return;

        Directory.CreateDirectory(directory);
        foreach (var (trackId, entries) in dirty)
            PersistTrack(directory, trackId, entries);
    }

    public void DeleteAllTrackFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var path in Directory.EnumerateFiles(directory, "track-*.json"))
        {
            try { File.Delete(path); }
            catch { /* best effort */ }
        }

        foreach (var path in Directory.EnumerateFiles(directory, "track-*.json.tmp"))
        {
            try { File.Delete(path); }
            catch { /* best effort */ }
        }
    }

    public static void PersistTrack(string directory, int trackId, List<ChronoEntry> entries)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"track-{trackId}.json");
        var tmpPath = path + ".tmp";

        if (entries.Count == 0)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { /* best effort */ }

            try
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch { /* best effort */ }

            return;
        }

        var json = JsonSerializer.Serialize(new ChronoDatabase { Sessions = entries }, JsonOptions);
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }

    public static List<ChronoEntry> CapEntries(IEnumerable<ChronoEntry> entries) =>
        entries
            .Where(s => s.BestLapMs is > 0)
            .OrderBy(s => s.BestLapMs)
            .ThenBy(s => s.StartedAt)
            .Take(MaxEntriesPerTrack)
            .ToList();

    public static List<ChronoEntry> ReadTrackFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var db = JsonSerializer.Deserialize<ChronoDatabase>(json, JsonOptions);
            return db?.Sessions ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static bool TryParseTrackId(string path, out int trackId)
    {
        trackId = -1;
        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith("track-", StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(name.AsSpan("track-".Length), out trackId) && trackId >= 0;
    }

    private IEnumerable<LeaderboardRow> RankedLaps(
        int trackId,
        bool bestPerPlayer = false,
        string? playerName = null)
    {
        List<ChronoEntry> snapshot;
        lock (_gate)
        {
            if (!_byTrack.TryGetValue(trackId, out var list) || list.Count == 0)
                return [];

            snapshot = list.ToList();
        }

        return LeaderboardQuery.ToRows(LeaderboardQuery.Filter(snapshot, bestPerPlayer, playerName));
    }

    private void MarkDirtyLocked(int trackId) => _dirty.Add(trackId);
}
