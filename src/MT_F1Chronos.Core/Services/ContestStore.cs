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
    private readonly Dictionary<string, Dictionary<int, List<ChronoEntry>>> _scores = new(StringComparer.Ordinal);
    private readonly HashSet<(string ContestId, int TrackId)> _dirty = [];
    private readonly object _gate = new();
    private readonly Timer _saveTimer;
    private bool _indexDirty;
    private bool _disposed;

    public ContestStore(string? dataDirectory = null)
    {
        var root = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos");
        _contestsDirectory = Path.Combine(root, "contests");
        _indexPath = Path.Combine(_contestsDirectory, "index.json");
        _saveTimer = new Timer(_ => FlushDirty(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Load()
    {
        Directory.CreateDirectory(_contestsDirectory);

        lock (_gate)
        {
            _contests.Clear();
            _scores.Clear();
            _dirty.Clear();
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
                var board = new Dictionary<int, List<ChronoEntry>>();
                var dir = ContestDirectory(contest.Id);
                if (Directory.Exists(dir))
                {
                    foreach (var path in Directory.EnumerateFiles(dir, "track-*.json"))
                    {
                        if (!TryParseTrackId(path, out var trackId))
                            continue;

                        var entries = CapEntries(ReadTrackFile(path));
                        if (entries.Count > 0)
                            board[trackId] = entries;
                    }
                }

                _scores[contest.Id] = board;
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
            _scores[contest.Id] = new Dictionary<int, List<ChronoEntry>>();
            Directory.CreateDirectory(ContestDirectory(contest.Id));
            _indexDirty = true;
            ScheduleSave();
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
            ScheduleSave();
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
            ScheduleSave();
        }

        FlushDirty();
        return true;
    }

    public bool Delete(string contestId)
    {
        lock (_gate)
        {
            var removed = _contests.RemoveAll(c => c.Id == contestId);
            if (removed == 0)
                return false;

            _scores.Remove(contestId);
            _dirty.RemoveWhere(d => d.ContestId == contestId);
            _indexDirty = true;
        }

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

    /// <summary>Writes the lap into every Active contest that accepts the track.</summary>
    public void RecordCompletedLap(string playerName, int trackId, string trackName, uint lapMs)
    {
        if (string.IsNullOrWhiteSpace(playerName) || trackId < 0 || lapMs == 0)
            return;

        lock (_gate)
        {
            foreach (var contest in _contests.Where(c => c.Status == ContestStatus.Active))
            {
                if (contest.TrackFilter is int filter && filter != trackId)
                    continue;

                if (!_scores.TryGetValue(contest.Id, out var board))
                {
                    board = new Dictionary<int, List<ChronoEntry>>();
                    _scores[contest.Id] = board;
                }

                if (!board.TryGetValue(trackId, out var list))
                {
                    list = [];
                    board[trackId] = list;
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

                if (list.Count > SessionStore.MaxEntriesPerTrack)
                {
                    var capped = CapEntries(list);
                    list.Clear();
                    list.AddRange(capped);
                }

                _dirty.Add((contest.Id, trackId));
            }

            if (_dirty.Count > 0)
                ScheduleSave();
        }
    }

    public IReadOnlyList<LeaderboardRow> GetLeaderboard(
        string contestId,
        int trackId,
        int count = LeaderboardSizes.Default,
        bool bestPerPlayer = false) =>
        RankedLaps(contestId, trackId, bestPerPlayer).Take(LeaderboardSizes.Normalize(count)).ToList();

    public IReadOnlyList<TrackSummary> GetTracksWithScores(string contestId)
    {
        lock (_gate)
        {
            if (!_scores.TryGetValue(contestId, out var board))
                return [];

            return board
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
        string contestId,
        int trackId,
        bool bestPerPlayer = false,
        string? playerName = null) =>
        RankedLaps(contestId, trackId, bestPerPlayer, playerName).ToList();

    public IReadOnlyList<string> GetPlayerNamesForTrack(string contestId, int trackId)
    {
        lock (_gate)
        {
            if (!_scores.TryGetValue(contestId, out var board) ||
                !board.TryGetValue(trackId, out var list))
                return [];

            return list
                .Where(s => s.BestLapMs is > 0 && !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => s.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool DeleteEntry(string contestId, string entryId)
    {
        if (string.IsNullOrWhiteSpace(contestId) || string.IsNullOrWhiteSpace(entryId))
            return false;

        int? dirtyTrack = null;
        lock (_gate)
        {
            if (!_scores.TryGetValue(contestId, out var board))
                return false;

            foreach (var (trackId, list) in board)
            {
                var removed = list.RemoveAll(s => string.Equals(s.Id, entryId, StringComparison.Ordinal));
                if (removed > 0)
                {
                    dirtyTrack = trackId;
                    break;
                }
            }
        }

        if (dirtyTrack is null)
            return false;

        _dirty.Add((contestId, dirtyTrack.Value));
        ScheduleSave();
        FlushDirty();
        return true;
    }

    public int DeletePlayerOnTrack(string contestId, string playerName, int trackId)
    {
        if (string.IsNullOrWhiteSpace(contestId) || string.IsNullOrWhiteSpace(playerName) || trackId < 0)
            return 0;

        int removed;
        lock (_gate)
        {
            if (!_scores.TryGetValue(contestId, out var board) ||
                !board.TryGetValue(trackId, out var list))
                return 0;

            removed = list.RemoveAll(s =>
                string.Equals(s.Name, playerName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (removed > 0)
        {
            _dirty.Add((contestId, trackId));
            ScheduleSave();
            FlushDirty();
        }

        return removed;
    }

    public IReadOnlyList<ChronoEntry> GetAllScoredEntries(string contestId)
    {
        lock (_gate)
        {
            if (!_scores.TryGetValue(contestId, out var board))
                return [];

            return board.Values
                .SelectMany(list => list)
                .Where(s => s.BestLapMs is > 0)
                .OrderBy(s => s.TrackName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.BestLapMs)
                .ThenBy(s => s.StartedAt)
                .ToList();
        }
    }

    public int EntryCount(string contestId)
    {
        lock (_gate)
        {
            return _scores.TryGetValue(contestId, out var board)
                ? board.Values.Sum(list => list.Count(s => s.BestLapMs is > 0))
                : 0;
        }
    }

    public IScoreBoardView AsScoreBoard(string contestId) => new ContestScoreBoardView(this, contestId);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        FlushDirty();
        _saveTimer.Dispose();
    }

    private IEnumerable<LeaderboardRow> RankedLaps(
        string contestId,
        int trackId,
        bool bestPerPlayer = false,
        string? playerName = null)
    {
        List<ChronoEntry> snapshot;
        lock (_gate)
        {
            if (!_scores.TryGetValue(contestId, out var board) ||
                !board.TryGetValue(trackId, out var list) ||
                list.Count == 0)
                return [];

            snapshot = list.ToList();
        }

        return LeaderboardQuery.ToRows(LeaderboardQuery.Filter(snapshot, bestPerPlayer, playerName));
    }

    private void ScheduleSave()
    {
        if (_disposed)
            return;

        try
        {
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
    }

    private void FlushDirty()
    {
        List<(string ContestId, int TrackId)> dirty;
        bool saveIndex;
        List<Contest> contestsSnapshot;
        Dictionary<(string, int), List<ChronoEntry>> trackSnapshots;

        lock (_gate)
        {
            if (_dirty.Count == 0 && !_indexDirty)
                return;

            dirty = _dirty.ToList();
            _dirty.Clear();
            saveIndex = _indexDirty;
            _indexDirty = false;
            contestsSnapshot = _contests.ToList();
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);

            trackSnapshots = dirty.ToDictionary(
                d => d,
                d =>
                {
                    if (_scores.TryGetValue(d.ContestId, out var board) &&
                        board.TryGetValue(d.TrackId, out var list))
                        return list.ToList();
                    return [];
                });
        }

        if (saveIndex)
            PersistIndex(contestsSnapshot);

        foreach (var key in dirty)
            PersistTrack(key.ContestId, key.TrackId, trackSnapshots[key]);
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

    private void PersistTrack(string contestId, int trackId, List<ChronoEntry> entries)
    {
        var dir = ContestDirectory(contestId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"track-{trackId}.json");
        var tmp = path + ".tmp";

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
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch { /* best effort */ }

            return;
        }

        var json = JsonSerializer.Serialize(new ChronoDatabase { Sessions = entries }, JsonOptions);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private string ContestDirectory(string contestId) =>
        Path.Combine(_contestsDirectory, contestId);

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
            .Take(SessionStore.MaxEntriesPerTrack)
            .ToList();

    private sealed class ContestScoreBoardView(ContestStore store, string contestId) : IScoreBoardView
    {
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
    }
}
