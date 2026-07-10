using System.Text.Json;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.Core.Services;

public static class LapTimeFormatter
{
    public static string Format(uint lapMs)
    {
        if (lapMs == 0)
            return "--:--.---";

        var totalSeconds = lapMs / 1000.0;
        var minutes = (int)(totalSeconds / 60);
        var seconds = totalSeconds - minutes * 60;

        return minutes > 0
            ? $"{minutes}:{seconds:00.000}"
            : $"{seconds:0.000}";
    }
}

public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private ChronoDatabase _database = new();
    private ChronoEntry? _activeSession;

    public SessionStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos",
            "sessions.json");
    }

    public ChronoEntry? ActiveSession => _activeSession;

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
        _activeSession = _database.Sessions.FirstOrDefault(s => s.IsActive);
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(_database, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public ChronoEntry StartSession(string name, int trackId, string trackName)
    {
        CloseActiveSession();

        var entry = new ChronoEntry
        {
            Name = name,
            TrackId = trackId,
            TrackName = trackName,
            StartedAt = DateTime.UtcNow,
            IsActive = true,
        };

        _database.Sessions.Add(entry);
        _activeSession = entry;
        Save();
        return entry;
    }

    public void UpdateActiveBest(uint lapMs)
    {
        if (_activeSession is null || lapMs == 0)
            return;

        if (!_activeSession.BestLapMs.HasValue || lapMs < _activeSession.BestLapMs)
        {
            _activeSession.BestLapMs = lapMs;
            Save();
        }
    }

    public void CloseActiveSession()
    {
        if (_activeSession is null)
            return;

        _activeSession.IsActive = false;
        _activeSession.EndedAt = DateTime.UtcNow;
        _activeSession = null;
        Save();
    }

    public IReadOnlyList<LeaderboardRow> GetTopThree(int trackId, int count = 3)
    {
        return _database.Sessions
            .Where(s => s.TrackId == trackId && s.BestLapMs.HasValue && s.BestLapMs > 0)
            .OrderBy(s => s.BestLapMs)
            .Take(count)
            .Select((s, i) => new LeaderboardRow
            {
                Rank = i + 1,
                Name = s.Name,
                BestLapMs = s.BestLapMs!.Value,
                FormattedTime = LapTimeFormatter.Format(s.BestLapMs.Value),
            })
            .ToList();
    }

    public IEnumerable<string> GetRecentNames(int limit = 8)
    {
        return _database.Sessions
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.Name)
            .Distinct()
            .Take(limit);
    }

    public int GetNextDefaultNumber()
    {
        var numbers = _database.Sessions
            .Select(s => s.Name)
            .Select(name =>
            {
                if (name.StartsWith("Chrono #", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(name["Chrono #".Length..], out var n))
                    return n;
                return 0;
            })
            .DefaultIfEmpty(0);

        return numbers.Max() + 1;
    }

    public OverlaySnapshot BuildSnapshot(TelemetryState state)
    {
        var topThree = state.TrackId >= 0
            ? GetTopThree(state.TrackId)
            : [];

        var currentBest = _activeSession?.BestLapMs ?? state.EffectiveBestLapMs;

        return new OverlaySnapshot
        {
            TrackName = state.TrackName,
            CurrentSessionName = _activeSession?.Name ?? "Session actuelle",
            CurrentBestFormatted = currentBest.HasValue
                ? LapTimeFormatter.Format(currentBest.Value)
                : "--:--.---",
            HasCurrentBest = currentBest.HasValue && currentBest > 0,
            TopThree = topThree,
            IsConnected = state.IsReceiving &&
                          (DateTime.UtcNow - state.LastPacketUtc).TotalSeconds < 3,
            IsTimeTrial = state.IsTimeTrial,
        };
    }
}
