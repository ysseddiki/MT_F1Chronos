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

    private readonly Dictionary<int, List<ChronoEntry>> _byTrack = new();
    private readonly HashSet<int> _dirty = new();
    private readonly object _gate = new();

    public event Action? BecameDirty;

    public void LoadFromDirectory(string directory)
    {
        Directory.CreateDirectory(directory);

        lock (_gate)
        {
            _byTrack.Clear();
            _dirty.Clear();

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

            list.Add(new ChronoEntry
            {
                Name = playerName.Trim(),
                TrackId = trackId,
                TrackName = trackName,
                BestLapMs = lapMs,
                StartedAt = DateTime.UtcNow,
                EndedAt = DateTime.UtcNow,
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

            removed = list.RemoveAll(s =>
                string.Equals(s.Name, playerName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
                MarkDirtyLocked(trackId);
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
            _byTrack.Clear();
            foreach (var trackId in trackIds)
                _dirty.Add(trackId);
        }

        BecameDirty?.Invoke();
        return removed;
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
