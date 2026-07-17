using System.Text.Json;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.Core.Services;

public sealed class SessionStore : IDisposable
{
    public const int MaxEntriesPerTrack = 5_000;

    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _dataDirectory;
    private readonly string _sessionsDirectory;
    private readonly string _legacyFilePath;
    private readonly Dictionary<int, List<ChronoEntry>> _byTrack = new();
    private readonly HashSet<int> _dirtyTracks = new();
    private readonly object _gate = new();
    private readonly Timer _saveTimer;

    private int _liveTrackId = -1;
    private string _liveTrackName = "Inconnu";
    private uint? _liveLastLapMs;
    private bool _disposed;

    public SessionStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos");
        _sessionsDirectory = Path.Combine(_dataDirectory, "sessions");
        _legacyFilePath = Path.Combine(_dataDirectory, "sessions.json");
        _saveTimer = new Timer(_ => FlushDirty(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool HasLiveSession => _liveTrackId >= 0;

    /// <summary>Directory that holds per-track score files (<c>track-{id}.json</c>).</summary>
    public string SessionsDirectoryPath => _sessionsDirectory;

    /// <summary>Alias kept for debug UI compatibility.</summary>
    public string SessionsFilePath => _sessionsDirectory;

    public SessionStoreDebugInfo BuildDebugInfo()
    {
        lock (_gate)
        {
            var total = _byTrack.Values.Sum(list => list.Count);
            var scored = _byTrack.Values.Sum(list => list.Count(s => s.BestLapMs is > 0));

            return new SessionStoreDebugInfo
            {
                HasActiveSession = HasLiveSession,
                ActiveTrackId = HasLiveSession ? _liveTrackId : null,
                ActiveTrackName = HasLiveSession ? _liveTrackName : null,
                ActiveBestLapMs = _liveLastLapMs,
                SessionsFilePath = _sessionsDirectory,
                TotalSessions = total,
                ScoredSessions = scored,
            };
        }
    }

    public void Load()
    {
        Directory.CreateDirectory(_sessionsDirectory);
        MigrateLegacyIfNeeded();

        lock (_gate)
        {
            _byTrack.Clear();
            _dirtyTracks.Clear();

            foreach (var path in Directory.EnumerateFiles(_sessionsDirectory, "track-*.json"))
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

    /// <summary>Flushes any pending dirty tracks immediately (called on app exit).</summary>
    public void Save() => FlushDirty();

    /// <summary>Tracks current circuit only — player name is never stored here.</summary>
    public void EnsureTrackContext(int trackId, string trackName)
    {
        if (trackId < 0)
            return;

        if (_liveTrackId != trackId)
            _liveLastLapMs = null;

        _liveTrackId = trackId;
        _liveTrackName = trackName;
    }

    /// <summary>Persists one valid completed lap under the player name at record time.</summary>
    public void RecordCompletedLap(string playerName, int trackId, string trackName, uint lapMs)
    {
        if (string.IsNullOrWhiteSpace(playerName) || trackId < 0 || lapMs == 0)
            return;

        EnsureTrackContext(trackId, trackName);

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

            _liveLastLapMs = lapMs;
            ScheduleSave(trackId);
        }
    }

    public void CloseActiveSession()
    {
        _liveTrackId = -1;
        _liveTrackName = "Inconnu";
        _liveLastLapMs = null;
    }

    public IReadOnlyList<LeaderboardRow> GetLeaderboard(int trackId, int count = LeaderboardSizes.Default) =>
        RankedLaps(trackId).Take(count).ToList();

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

    public IReadOnlyList<LeaderboardRow> GetScoresForTrack(int trackId) =>
        RankedLaps(trackId).ToList();

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

    public int ClearScoresForTrack(int trackId)
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
            _dirtyTracks.Remove(trackId);
        }

        if (removed > 0)
            DeleteTrackFile(trackId);

        return removed;
    }

    public int ClearAllScores()
    {
        int removed;
        lock (_gate)
        {
            removed = _byTrack.Values.Sum(list => list.Count);
            if (removed == 0)
                return 0;

            _byTrack.Clear();
            _dirtyTracks.Clear();
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        foreach (var path in Directory.EnumerateFiles(_sessionsDirectory, "track-*.json"))
        {
            try { File.Delete(path); }
            catch { /* best effort */ }
        }

        foreach (var path in Directory.EnumerateFiles(_sessionsDirectory, "track-*.json.tmp"))
        {
            try { File.Delete(path); }
            catch { /* best effort */ }
        }

        return removed;
    }

    public OverlaySnapshot BuildSnapshot(TelemetryState state, string playerName, int leaderboardSize = LeaderboardSizes.Default)
    {
        var trackId = ResolveOverlayTrackId(state);
        var size = LeaderboardSizes.Normalize(leaderboardSize);
        var leaderboard = trackId >= 0 ? GetLeaderboard(trackId, size) : [];
        var currentLap = state.CurrentLapTimeMs;

        return new OverlaySnapshot
        {
            TrackName = ResolveOverlayTrackName(state, trackId),
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? "Joueur" : playerName,
            CurrentLapFormatted = currentLap is > 0
                ? LapTimeFormatter.Format(currentLap.Value)
                : "--:--.---",
            HasCurrentLap = currentLap is > 0,
            LeaderboardSize = size,
            Leaderboard = leaderboard,
            IsConnected = state.IsReceiving &&
                          (DateTime.UtcNow - state.LastPacketUtc).TotalSeconds < 3,
            IsTimeTrial = state.IsTimeTrial,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        FlushDirty();
        _saveTimer.Dispose();
    }

    /// <summary>
    /// Live telemetry track always wins once known.
    /// Fall back to last scored track only at startup (no UDP yet).
    /// </summary>
    private int ResolveOverlayTrackId(TelemetryState state)
    {
        if (state.TrackId >= 0)
            return state.TrackId;

        if (_liveTrackId >= 0)
            return _liveTrackId;

        return MostRecentScoredTrackId();
    }

    private int MostRecentScoredTrackId()
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

    private string ResolveOverlayTrackName(TelemetryState state, int trackId)
    {
        if (trackId < 0)
            return "—";

        if (state.TrackId == trackId)
            return state.TrackName;

        lock (_gate)
        {
            if (_byTrack.TryGetValue(trackId, out var list))
            {
                var storedName = list
                    .Where(s => !string.IsNullOrWhiteSpace(s.TrackName))
                    .OrderByDescending(s => s.StartedAt)
                    .Select(s => s.TrackName)
                    .FirstOrDefault();

                if (storedName is not null)
                    return storedName;
            }
        }

        return F1UdpConstants.GetTrackName(trackId);
    }

    private IEnumerable<LeaderboardRow> RankedLaps(int trackId)
    {
        List<ChronoEntry> snapshot;
        lock (_gate)
        {
            if (!_byTrack.TryGetValue(trackId, out var list) || list.Count == 0)
                return [];

            snapshot = list.ToList();
        }

        return snapshot
            .Where(s => s.BestLapMs is > 0)
            .OrderBy(s => s.BestLapMs)
            .ThenBy(s => s.StartedAt)
            .Select((s, i) => new LeaderboardRow
            {
                Rank = i + 1,
                Name = s.Name,
                BestLapMs = s.BestLapMs!.Value,
                FormattedTime = LapTimeFormatter.Format(s.BestLapMs.Value),
            });
    }

    private void ScheduleSave(int trackId)
    {
        if (_disposed)
            return;

        _dirtyTracks.Add(trackId);
        try
        {
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // App is shutting down; Dispose() will flush remaining state.
        }
    }

    private void FlushDirty()
    {
        List<int> dirty;
        lock (_gate)
        {
            if (_dirtyTracks.Count == 0)
                return;

            dirty = _dirtyTracks.ToList();
            _dirtyTracks.Clear();
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        // Re-read under lock so a concurrent Clear* cannot resurrect deleted files.
        foreach (var trackId in dirty)
        {
            List<ChronoEntry> entries;
            lock (_gate)
            {
                entries = _byTrack.TryGetValue(trackId, out var list)
                    ? list.ToList()
                    : [];
            }

            PersistTrack(trackId, entries);
        }
    }

    private void PersistTrack(int trackId, List<ChronoEntry> entries)
    {
        Directory.CreateDirectory(_sessionsDirectory);

        if (entries.Count == 0)
        {
            DeleteTrackFile(trackId);
            return;
        }

        var path = TrackFilePath(trackId);
        var tmpPath = path + ".tmp";
        var payload = new ChronoDatabase { Sessions = entries };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }

    private void DeleteTrackFile(int trackId)
    {
        var path = TrackFilePath(trackId);
        var tmpPath = path + ".tmp";

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
    }

    private string TrackFilePath(int trackId) =>
        Path.Combine(_sessionsDirectory, $"track-{trackId}.json");

    private void MigrateLegacyIfNeeded()
    {
        if (!File.Exists(_legacyFilePath))
            return;

        ChronoDatabase? legacy;
        try
        {
            var json = File.ReadAllText(_legacyFilePath);
            legacy = JsonSerializer.Deserialize<ChronoDatabase>(json, JsonOptions);
        }
        catch
        {
            return;
        }

        if (legacy is null)
            return;

        foreach (var group in legacy.Sessions.GroupBy(s => s.TrackId))
        {
            if (group.Key < 0)
                continue;

            var capped = CapEntries(group.ToList());
            PersistTrack(group.Key, capped);
        }

        try
        {
            var bak = _legacyFilePath + ".bak";
            if (File.Exists(bak))
                File.Delete(bak);
            File.Move(_legacyFilePath, bak);
        }
        catch
        {
            // Migration files were written; leaving the legacy file is preferable to data loss.
        }
    }

    private static List<ChronoEntry> ReadTrackFile(string path)
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

    private static bool TryParseTrackId(string path, out int trackId)
    {
        trackId = -1;
        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith("track-", StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(name.AsSpan("track-".Length), out trackId) && trackId >= 0;
    }

    private static List<ChronoEntry> CapEntries(IEnumerable<ChronoEntry> entries) =>
        entries
            .Where(s => s.BestLapMs is > 0)
            .OrderBy(s => s.BestLapMs)
            .ThenBy(s => s.StartedAt)
            .Take(MaxEntriesPerTrack)
            .ToList();
}
