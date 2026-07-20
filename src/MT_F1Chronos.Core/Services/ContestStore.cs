using System.Text.Json;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.Core.Services;

public sealed class ContestStore : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _contestsDirectory;
    private readonly string _indexPath;
    private readonly List<Contest> _contests = [];
    private readonly Dictionary<string, TrackScoreBoard> _boards = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly DeferredFlush _flush;
    private bool _indexDirty;
    private bool _disposed;

    public ContestStore(string? dataDirectory = null)
    {
        var root = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos");
        _contestsDirectory = Path.Combine(root, "contests");
        _indexPath = Path.Combine(_contestsDirectory, "index.json");
        _flush = new DeferredFlush(SaveDelay, FlushDirty);
    }

    public void Load()
    {
        Directory.CreateDirectory(_contestsDirectory);

        lock (_gate)
        {
            _contests.Clear();
            foreach (var board in _boards.Values)
                board.BecameDirty -= OnBoardBecameDirty;
            _boards.Clear();
            _indexDirty = false;

            if (File.Exists(_indexPath))
            {
                try
                {
                    var json = File.ReadAllText(_indexPath);
                    var index = JsonSerializer.Deserialize<ContestIndex>(json, JsonOptions);
                    if (index?.Contests is { Count: > 0 })
                        _contests.AddRange(index.Contests);
                }
                catch
                {
                    // Keep empty index on corrupt file.
                }
            }

            foreach (var contest in _contests)
            {
                var board = new TrackScoreBoard();
                board.BecameDirty += OnBoardBecameDirty;
                board.LoadFromDirectory(ContestDirectory(contest.Id));
                _boards[contest.Id] = board;
            }
        }
    }

    public void Save() => FlushDirty();

    public IReadOnlyList<Contest> List()
    {
        lock (_gate)
            return _contests.OrderByDescending(c => c.CreatedAt).ToList();
    }

    public Contest? Get(string contestId)
    {
        lock (_gate)
            return _contests.FirstOrDefault(c => c.Id == contestId);
    }

    public Contest Create(string name, bool startImmediately = true)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Contest name is required.", nameof(name));

        var contest = new Contest
        {
            Name = trimmed,
            Status = startImmediately ? ContestStatus.Active : ContestStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            StartedAt = startImmediately ? DateTime.UtcNow : null,
        };

        lock (_gate)
        {
            _contests.Add(contest);
            var board = new TrackScoreBoard();
            board.BecameDirty += OnBoardBecameDirty;
            _boards[contest.Id] = board;
            Directory.CreateDirectory(ContestDirectory(contest.Id));
            _indexDirty = true;
            _flush.Schedule();
        }

        FlushDirty();
        return contest;
    }

    public bool Start(string contestId)
    {
        lock (_gate)
        {
            var contest = _contests.FirstOrDefault(c => c.Id == contestId);
            if (contest is null)
                return false;

            if (contest.Status == ContestStatus.Active)
                return true;

            contest.Status = ContestStatus.Active;
            contest.StartedAt ??= DateTime.UtcNow;
            contest.StoppedAt = null;
            _indexDirty = true;
            _flush.Schedule();
        }

        FlushDirty();
        return true;
    }

    public bool Stop(string contestId)
    {
        lock (_gate)
        {
            var contest = _contests.FirstOrDefault(c => c.Id == contestId);
            if (contest is null)
                return false;

            if (contest.Status == ContestStatus.Stopped)
                return true;

            contest.Status = ContestStatus.Stopped;
            contest.StoppedAt = DateTime.UtcNow;
            _indexDirty = true;
            _flush.Schedule();
        }

        FlushDirty();
        return true;
    }

    public bool Delete(string contestId)
    {
        TrackScoreBoard? board;
        lock (_gate)
        {
            var removed = _contests.RemoveAll(c => c.Id == contestId);
            if (removed == 0)
                return false;

            _boards.Remove(contestId, out board);
            _indexDirty = true;
        }

        if (board is not null)
            board.BecameDirty -= OnBoardBecameDirty;

        FlushDirty();

        var dir = ContestDirectory(contestId);
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best effort
        }

        return true;
    }

    public void RecordCompletedLap(string playerName, int trackId, string trackName, uint lapMs)
    {
        if (string.IsNullOrWhiteSpace(playerName) || trackId < 0 || lapMs == 0)
            return;

        List<TrackScoreBoard> targets;
        lock (_gate)
        {
            targets = _contests
                .Where(c => c.Status == ContestStatus.Active)
                .Where(c => c.TrackFilter is not int filter || filter == trackId)
                .Select(c => _boards.TryGetValue(c.Id, out var board) ? board : null)
                .Where(b => b is not null)
                .Select(b => b!)
                .ToList();
        }

        foreach (var board in targets)
            board.Record(playerName, trackId, trackName, lapMs);
    }

    public IReadOnlyList<LeaderboardRow> GetLeaderboard(
        string contestId,
        int trackId,
        int count = LeaderboardSizes.Default,
        bool bestPerPlayer = false) =>
        GetBoard(contestId)?.GetLeaderboard(trackId, count, bestPerPlayer) ?? [];

    public IReadOnlyList<TrackSummary> GetTracksWithScores(string contestId) =>
        GetBoard(contestId)?.GetTracksWithScores() ?? [];

    public IReadOnlyList<LeaderboardRow> GetScoresForTrack(
        string contestId,
        int trackId,
        bool bestPerPlayer = false,
        string? playerName = null) =>
        GetBoard(contestId)?.GetScoresForTrack(trackId, bestPerPlayer, playerName) ?? [];

    public IReadOnlyList<string> GetPlayerNamesForTrack(string contestId, int trackId) =>
        GetBoard(contestId)?.GetPlayerNamesForTrack(trackId) ?? [];

    public bool DeleteEntry(string contestId, string entryId)
    {
        var board = GetBoard(contestId);
        if (board is null || !board.DeleteEntry(entryId))
            return false;

        FlushDirty();
        return true;
    }

    public int DeletePlayerOnTrack(string contestId, string playerName, int trackId)
    {
        var board = GetBoard(contestId);
        if (board is null)
            return 0;

        var removed = board.DeletePlayerOnTrack(playerName, trackId);
        if (removed > 0)
            FlushDirty();
        return removed;
    }

    public int ClearScoresForTrack(string contestId, int trackId)
    {
        var board = GetBoard(contestId);
        if (board is null)
            return 0;

        var removed = board.ClearTrack(trackId);
        if (removed > 0)
            FlushDirty();
        return removed;
    }

    public int ClearAllScores(string contestId)
    {
        var board = GetBoard(contestId);
        if (board is null)
            return 0;

        var removed = board.ClearAll();
        if (removed == 0)
            return 0;

        FlushDirty();
        board.DeleteAllTrackFiles(ContestDirectory(contestId));
        return removed;
    }

    public IReadOnlyList<ChronoEntry> GetAllScoredEntries(string contestId) =>
        GetBoard(contestId)?.GetAllScoredEntries() ?? [];

    public int EntryCount(string contestId) => GetBoard(contestId)?.EntryCount ?? 0;

    public IScoreBoardView AsScoreBoard(string contestId) => new ContestScoreBoardView(this, contestId);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        FlushDirty();

        lock (_gate)
        {
            foreach (var board in _boards.Values)
                board.BecameDirty -= OnBoardBecameDirty;
        }

        _flush.Dispose();
    }

    private TrackScoreBoard? GetBoard(string contestId)
    {
        lock (_gate)
            return _boards.TryGetValue(contestId, out var board) ? board : null;
    }

    private void OnBoardBecameDirty() => _flush.Schedule();

    private void FlushDirty()
    {
        bool saveIndex;
        List<Contest> contestsSnapshot;
        List<(string ContestId, TrackScoreBoard Board)> boards;

        lock (_gate)
        {
            saveIndex = _indexDirty;
            _indexDirty = false;
            contestsSnapshot = _contests.ToList();
            boards = _boards.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        if (saveIndex)
            PersistIndex(contestsSnapshot);

        foreach (var (contestId, board) in boards)
        {
            if (board.HasDirty)
                board.PersistDirty(ContestDirectory(contestId));
        }
    }

    private void PersistIndex(List<Contest> contests)
    {
        Directory.CreateDirectory(_contestsDirectory);
        var path = _indexPath;
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(new ContestIndex { Contests = contests }, JsonOptions);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private string ContestDirectory(string contestId) =>
        Path.Combine(_contestsDirectory, contestId);

    private sealed class ContestScoreBoardView(ContestStore store, string contestId) : IScoreBoardView
    {
        public string BoardLabel =>
            store.Get(contestId) is { Name: { Length: > 0 } name }
                ? $"Concours — {name}"
                : "Concours";

        public IReadOnlyList<TrackSummary> GetTracksWithScores() => store.GetTracksWithScores(contestId);

        public IReadOnlyList<LeaderboardRow> GetScoresForTrack(
            int trackId,
            bool bestPerPlayer = false,
            string? playerName = null) =>
            store.GetScoresForTrack(contestId, trackId, bestPerPlayer, playerName);

        public IReadOnlyList<string> GetPlayerNamesForTrack(int trackId) =>
            store.GetPlayerNamesForTrack(contestId, trackId);

        public bool DeleteEntry(string entryId) => store.DeleteEntry(contestId, entryId);

        public int DeletePlayerOnTrack(string playerName, int trackId) =>
            store.DeletePlayerOnTrack(contestId, playerName, trackId);

        public int ClearTrack(int trackId) => store.ClearScoresForTrack(contestId, trackId);

        public int ClearAll() => store.ClearAllScores(contestId);
    }
}
