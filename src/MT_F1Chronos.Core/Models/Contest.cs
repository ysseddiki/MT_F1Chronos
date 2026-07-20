namespace MT_F1Chronos.Core.Models;

public enum ContestStatus
{
    Draft = 0,
    Active = 1,
    Stopped = 2,
}

public sealed class Contest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public ContestStatus Status { get; set; } = ContestStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }

    /// <summary>When set, only laps on this track are recorded into the contest. Null = all tracks.</summary>
    public int? TrackFilter { get; set; }
}

public sealed class ContestIndex
{
    public List<Contest> Contests { get; set; } = [];
}

/// <summary>Minimal score-board view used by ScoresWindow for global or contest data.</summary>
public interface IScoreBoardView
{
    IReadOnlyList<TrackSummary> GetTracksWithScores();
    IReadOnlyList<LeaderboardRow> GetScoresForTrack(int trackId, bool bestPerPlayer = false, string? playerName = null);
    IReadOnlyList<string> GetPlayerNamesForTrack(int trackId);
    bool DeleteEntry(string entryId);
    int DeletePlayerOnTrack(string playerName, int trackId);
}
