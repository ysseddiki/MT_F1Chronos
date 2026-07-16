using System.Text.Json;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.Core.Services;

public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private ChronoDatabase _database = new();

    private int _liveTrackId = -1;
    private string _liveTrackName = "Inconnu";
    private uint? _liveLastLapMs;

    public SessionStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos",
            "sessions.json");
    }

    public bool HasLiveSession => _liveTrackId >= 0;
    public string SessionsFilePath => _filePath;

    public SessionStoreDebugInfo BuildDebugInfo() => new()
    {
        HasActiveSession = HasLiveSession,
        ActiveTrackId = HasLiveSession ? _liveTrackId : null,
        ActiveTrackName = HasLiveSession ? _liveTrackName : null,
        ActiveBestLapMs = _liveLastLapMs,
        SessionsFilePath = _filePath,
        TotalSessions = _database.Sessions.Count,
        ScoredSessions = _database.Sessions.Count(s => s.BestLapMs is > 0),
    };

    public void Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        if (!File.Exists(_filePath))
        {
            _database = new ChronoDatabase();
            return;
        }

        var json = File.ReadAllText(_filePath);
        _database = JsonSerializer.Deserialize<ChronoDatabase>(json, JsonOptions) ?? new ChronoDatabase();

        var dirty = false;
        foreach (var session in _database.Sessions.Where(s => s.IsActive))
        {
            session.IsActive = false;
            session.EndedAt ??= DateTime.UtcNow;
            dirty = true;
        }

        if (dirty)
            Save();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_database, JsonOptions));
    }

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

        _database.Sessions.Add(new ChronoEntry
        {
            Name = playerName.Trim(),
            TrackId = trackId,
            TrackName = trackName,
            BestLapMs = lapMs,
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
        });

        _liveLastLapMs = lapMs;
        Save();
    }

    public void CloseActiveSession()
    {
        _liveTrackId = -1;
        _liveTrackName = "Inconnu";
        _liveLastLapMs = null;
    }

    public IReadOnlyList<LeaderboardRow> GetLeaderboard(int trackId, int count = 5) =>
        RankedLaps(trackId).Take(count).ToList();

    public IReadOnlyList<TrackSummary> GetTracksWithScores() =>
        _database.Sessions
            .Where(s => s.BestLapMs is > 0)
            .GroupBy(s => s.TrackId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(s => s.StartedAt).First();
                return new TrackSummary
                {
                    TrackId = g.Key,
                    TrackName = latest.TrackName,
                    ScoreCount = g.Count(),
                };
            })
            .OrderBy(t => t.TrackName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<LeaderboardRow> GetScoresForTrack(int trackId) =>
        RankedLaps(trackId).ToList();

    public IReadOnlyList<ChronoEntry> GetAllScoredEntries() =>
        _database.Sessions
            .Where(s => s.BestLapMs is > 0)
            .OrderBy(s => s.TrackName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.BestLapMs)
            .ThenBy(s => s.StartedAt)
            .ToList();

    public int ClearScoresForTrack(int trackId)
    {
        if (trackId < 0)
            return 0;

        var removed = _database.Sessions.RemoveAll(s => s.TrackId == trackId);
        if (removed > 0)
            Save();
        return removed;
    }

    public int ClearAllScores()
    {
        var removed = _database.Sessions.Count;
        if (removed == 0)
            return 0;

        _database.Sessions.Clear();
        Save();
        return removed;
    }

    public OverlaySnapshot BuildSnapshot(TelemetryState state, string playerName)
    {
        var trackId = ResolveOverlayTrackId(state);
        var topFive = trackId >= 0 ? GetLeaderboard(trackId) : [];
        var currentLap = state.CurrentLapTimeMs;

        uint? p1Ms = null;
        if (trackId >= 0)
        {
            // Prefer live track P1 when driving; otherwise use overlay track leaderboard.
            var p1TrackId = state.TrackId >= 0 ? state.TrackId : trackId;
            p1Ms = RankedLaps(p1TrackId).Select(r => (uint?)r.BestLapMs).FirstOrDefault();
        }

        var hasDelta = currentLap is > 0 && p1Ms is > 0;
        var deltaMs = hasDelta ? (int)currentLap!.Value - (int)p1Ms!.Value : 0;

        return new OverlaySnapshot
        {
            TrackName = ResolveOverlayTrackName(state, trackId),
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? "Joueur" : playerName,
            CurrentLapFormatted = currentLap is > 0
                ? LapTimeFormatter.Format(currentLap.Value)
                : "--:--.---",
            HasCurrentLap = currentLap is > 0,
            HasDelta = hasDelta,
            DeltaFormatted = hasDelta ? LapTimeFormatter.FormatDelta(deltaMs) : string.Empty,
            IsAheadOfP1 = hasDelta && deltaMs < 0,
            TopFive = topFive,
            IsConnected = state.IsReceiving &&
                          (DateTime.UtcNow - state.LastPacketUtc).TotalSeconds < 3,
            IsTimeTrial = state.IsTimeTrial,
        };
    }

    /// <summary>
    /// Prefer a track that actually has scores so TOP 5 stays populated after restart.
    /// Live track wins only when it already has persisted laps.
    /// </summary>
    private int ResolveOverlayTrackId(TelemetryState state)
    {
        if (state.TrackId >= 0 && HasScoresForTrack(state.TrackId))
            return state.TrackId;

        if (_liveTrackId >= 0 && HasScoresForTrack(_liveTrackId))
            return _liveTrackId;

        var mostRecent = MostRecentScoredTrackId();
        if (mostRecent >= 0)
            return mostRecent;

        // No scores yet: keep live track (empty TOP 5 until first lap).
        if (state.TrackId >= 0)
            return state.TrackId;

        return _liveTrackId;
    }

    private bool HasScoresForTrack(int trackId) =>
        trackId >= 0 && _database.Sessions.Any(s => s.TrackId == trackId && s.BestLapMs is > 0);

    private int MostRecentScoredTrackId() =>
        _database.Sessions
            .Where(s => s.BestLapMs is > 0)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => (int?)s.TrackId)
            .FirstOrDefault() ?? -1;

    private string ResolveOverlayTrackName(TelemetryState state, int trackId)
    {
        if (trackId < 0)
            return "—";

        // If live telemetry matches the TOP 5 track, use live name.
        if (state.TrackId == trackId)
            return state.TrackName;

        var storedName = _database.Sessions
            .Where(s => s.TrackId == trackId && !string.IsNullOrWhiteSpace(s.TrackName))
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.TrackName)
            .FirstOrDefault();

        return storedName ?? F1UdpConstants.GetTrackName(trackId);
    }

    private IEnumerable<LeaderboardRow> RankedLaps(int trackId) =>
        _database.Sessions
            .Where(s => s.TrackId == trackId && s.BestLapMs is > 0)
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
