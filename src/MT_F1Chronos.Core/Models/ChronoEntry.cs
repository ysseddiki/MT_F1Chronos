namespace MT_F1Chronos.Core.Models;

public sealed class ChronoEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int TrackId { get; set; } = -1;
    public string TrackName { get; set; } = "Inconnu";
    public uint? BestLapMs { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ChronoDatabase
{
    public List<ChronoEntry> Sessions { get; set; } = [];
}

public sealed class LeaderboardRow
{
    public int Rank { get; init; }
    public string Name { get; init; } = string.Empty;
    public uint BestLapMs { get; init; }
    public string FormattedTime { get; init; } = "--:--.---";
}

public sealed class TrackSummary
{
    public int TrackId { get; init; }
    public string TrackName { get; init; } = string.Empty;
    public int ScoreCount { get; init; }
}

public sealed class OverlaySnapshot
{
    public string TrackName { get; init; } = "—";
    public string PlayerName { get; init; } = "Joueur";
    public string CurrentLapFormatted { get; init; } = "--:--.---";
    public bool HasCurrentLap { get; init; }
    public string DeltaFormatted { get; init; } = string.Empty;
    public bool HasDelta { get; init; }
    /// <summary>True when current lap is ahead of P1 (negative delta).</summary>
    public bool IsAheadOfP1 { get; init; }
    public IReadOnlyList<LeaderboardRow> TopFive { get; init; } = [];
    public bool IsConnected { get; init; }
    public bool IsTimeTrial { get; init; }
}
