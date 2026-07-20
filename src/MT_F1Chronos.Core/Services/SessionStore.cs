using System.Text.Json;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.Core.Services;

public sealed class SessionStore : IDisposable, IScoreBoardView
{
    public const int MaxEntriesPerTrack = TrackScoreBoard.MaxEntriesPerTrack;

    public string BoardLabel => "Global";

    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _dataDirectory;
    private readonly string _sessionsDirectory;
    private readonly string _legacyFilePath;
    private readonly TrackScoreBoard _board = new();
    private readonly object _flushGate = new();
    private readonly DeferredFlush _flush;

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
        _flush = new DeferredFlush(SaveDelay, FlushDirty);
        _board.BecameDirty += () => _flush.Schedule();
    }

    public bool HasLiveSession => _liveTrackId >= 0;

    public string SessionsDirectoryPath => _sessionsDirectory;

    public string SessionsFilePath => _sessionsDirectory;

    public SessionStoreDebugInfo BuildDebugInfo()
    {
        var entries = _board.GetAllScoredEntries();
        return new SessionStoreDebugInfo
        {
            HasActiveSession = HasLiveSession,
            ActiveTrackId = HasLiveSession ? _liveTrackId : null,
            ActiveTrackName = HasLiveSession ? _liveTrackName : null,
            ActiveBestLapMs = _liveLastLapMs,
            SessionsFilePath = _sessionsDirectory,
            TotalSessions = entries.Count,
            ScoredSessions = entries.Count,
        };
    }

    public void Load()
    {
        Directory.CreateDirectory(_sessionsDirectory);
        MigrateLegacyIfNeeded();
        _board.LoadFromDirectory(_sessionsDirectory);
    }

    public void Save() => FlushDirty();

    public void EnsureTrackContext(int trackId, string trackName)
    {
        if (trackId < 0)
            return;

        if (_liveTrackId != trackId)
            _liveLastLapMs = null;

        _liveTrackId = trackId;
        _liveTrackName = trackName;
    }

    public void RecordCompletedLap(string playerName, int trackId, string trackName, uint lapMs)
    {
        if (string.IsNullOrWhiteSpace(playerName) || trackId < 0 || lapMs == 0)
            return;

        EnsureTrackContext(trackId, trackName);
        _board.Record(playerName, trackId, trackName, lapMs);
        _liveLastLapMs = lapMs;
    }

    public void CloseActiveSession()
    {
        _liveTrackId = -1;
        _liveTrackName = "Inconnu";
        _liveLastLapMs = null;
    }

    public IReadOnlyList<LeaderboardRow> GetLeaderboard(
        int trackId,
        int count = LeaderboardSizes.Default,
        bool bestPerPlayer = false) =>
        _board.GetLeaderboard(trackId, count, bestPerPlayer);

    public IReadOnlyList<TrackSummary> GetTracksWithScores() => _board.GetTracksWithScores();

    public IReadOnlyList<LeaderboardRow> GetScoresForTrack(
        int trackId,
        bool bestPerPlayer = false,
        string? playerName = null) =>
        _board.GetScoresForTrack(trackId, bestPerPlayer, playerName);

    public IReadOnlyList<string> GetPlayerNamesForTrack(int trackId) =>
        _board.GetPlayerNamesForTrack(trackId);

    public bool DeleteEntry(string entryId)
    {
        if (!_board.DeleteEntry(entryId))
            return false;

        FlushDirty();
        return true;
    }

    public int DeletePlayerOnTrack(string playerName, int trackId)
    {
        var removed = _board.DeletePlayerOnTrack(playerName, trackId);
        if (removed > 0)
            FlushDirty();
        return removed;
    }

    public IReadOnlyList<string> GetRecentPlayerNames(int max = 10) =>
        _board.GetRecentPlayerNames(max);

    public IReadOnlyList<ChronoEntry> GetAllScoredEntries() => _board.GetAllScoredEntries();

    public int ClearTrack(int trackId) => ClearScoresForTrack(trackId);

    public int ClearAll() => ClearAllScores();

    public int ClearScoresForTrack(int trackId)
    {
        var removed = _board.ClearTrack(trackId);
        if (removed > 0)
            FlushDirty();
        return removed;
    }

    public int ClearAllScores()
    {
        var removed = _board.ClearAll();
        if (removed == 0)
            return 0;

        FlushDirty();
        _board.DeleteAllTrackFiles(_sessionsDirectory);
        return removed;
    }

    public OverlaySnapshot BuildSnapshot(
        TelemetryState state,
        string playerName,
        int leaderboardSize = LeaderboardSizes.Default,
        bool showGlobalLeaderboard = true,
        bool showContestLeaderboard = false,
        string contestLabel = "",
        int contestLeaderboardSize = LeaderboardSizes.Extended,
        IReadOnlyList<LeaderboardRow>? contestLeaderboard = null,
        bool bestPerPlayer = false)
    {
        var trackId = ResolveOverlayTrackId(state);
        var size = LeaderboardSizes.Normalize(leaderboardSize);
        var contestSize = LeaderboardSizes.Normalize(contestLeaderboardSize);
        var leaderboard = trackId >= 0 ? GetLeaderboard(trackId, size, bestPerPlayer) : [];
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
            ShowGlobalLeaderboard = showGlobalLeaderboard,
            ShowContestLeaderboard = showContestLeaderboard,
            ContestLabel = contestLabel,
            ContestLeaderboardSize = contestSize,
            ContestLeaderboard = contestLeaderboard ?? [],
            BestPerPlayer = bestPerPlayer,
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
        _flush.Dispose();
    }

    public int ResolveOverlayTrackId(TelemetryState state)
    {
        if (state.TrackId >= 0)
            return state.TrackId;

        if (_liveTrackId >= 0)
            return _liveTrackId;

        return _board.MostRecentScoredTrackId();
    }

    private string ResolveOverlayTrackName(TelemetryState state, int trackId)
    {
        if (trackId < 0)
            return "—";

        if (state.TrackId == trackId)
            return state.TrackName;

        return _board.GetStoredTrackName(trackId) ?? F1UdpConstants.GetTrackName(trackId);
    }

    private void FlushDirty()
    {
        lock (_flushGate)
            _board.PersistDirty(_sessionsDirectory);
    }

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

            var capped = TrackScoreBoard.CapEntries(group.ToList());
            TrackScoreBoard.PersistTrack(_sessionsDirectory, group.Key, capped);
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
}
