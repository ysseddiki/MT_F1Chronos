using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.Core.Services;

public sealed class SessionStore : IDisposable, IScoreBoardView
{
    public const int MaxEntriesPerTrack = TrackScoreBoard.MaxEntriesPerTrack;

    public string BoardLabel => "Global";

    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private readonly string _dataDirectory;
    private readonly LocalChronosDb _db;
    private readonly TrackScoreBoard _board;
    private readonly object _flushGate = new();
    private readonly DeferredFlush _flush;
    private string _migrationStatus = "";

    private int _liveTrackId = -1;
    private string _liveTrackName = "Inconnu";
    private uint? _liveLastLapMs;
    private bool _disposed;

    public SessionStore(string? dataDirectory = null, TimeProvider? time = null)
    {
        _dataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos");
        _db = LocalChronosDb.Open(_dataDirectory);
        _board = new TrackScoreBoard(time, _db, contestId: null);
        _flush = new DeferredFlush(SaveDelay, FlushDirty);
        _board.BecameDirty += () => _flush.Schedule();
    }

    public bool HasLiveSession => _liveTrackId >= 0;

    public string DataDirectoryPath => _dataDirectory;

    public string DatabasePath => _db.DatabasePath;

    /// <summary>Legacy path kept for debug UI (now points at the SQLite file).</summary>
    public string SessionsDirectoryPath => _db.DatabasePath;

    public string SessionsFilePath => _db.DatabasePath;

    public string MigrationStatus => _migrationStatus;

    public LocalChronosDb Database => _db;

    public SessionStoreDebugInfo BuildDebugInfo()
    {
        var entries = _board.GetAllScoredEntries();
        return new SessionStoreDebugInfo
        {
            HasActiveSession = HasLiveSession,
            ActiveTrackId = HasLiveSession ? _liveTrackId : null,
            ActiveTrackName = HasLiveSession ? _liveTrackName : null,
            ActiveBestLapMs = _liveLastLapMs,
            SessionsFilePath = _db.DatabasePath,
            TotalSessions = entries.Count,
            ScoredSessions = entries.Count,
        };
    }

    public void Load()
    {
        Directory.CreateDirectory(_dataDirectory);
        _migrationStatus = LocalChronosMigrator.MigrateIfNeeded(_dataDirectory, _db);
        _board.LoadFromStore();
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

    public void RecordCompletedLap(
        string playerName, int trackId, string trackName, uint lapMs, bool customSetup = false)
    {
        if (string.IsNullOrWhiteSpace(playerName) || trackId < 0 || lapMs == 0)
            return;

        EnsureTrackContext(trackId, trackName);
        _board.Record(playerName, trackId, trackName, lapMs, customSetup);
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

    public bool RenameEntry(string entryId, string newName) => _board.RenameEntry(entryId, newName);

    public int RenamePlayer(string oldName, string newName) => _board.RenamePlayer(oldName, newName);

    public bool RestoreEntry(ChronoEntry entry) => _board.RestoreEntry(entry);

    public IReadOnlyList<string> PeekDeletedIds() => _board.PeekDeletedIds();

    public void AcknowledgeDeletedIds(IEnumerable<string> ids) => _board.AcknowledgeDeletedIds(ids);

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
        _board.DeleteAllPersisted();
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
        bool bestPerPlayer = false,
        bool countCustomSetupLaps = true)
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
            CountCustomSetupLaps = countCustomSetupLaps,
            HasCustomSetup = state.HasCustomSetup,
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
        _db.Dispose();
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
            _board.PersistDirty();
    }
}
