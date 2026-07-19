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
    public int LeaderboardSize { get; init; } = LeaderboardSizes.Default;
    public IReadOnlyList<LeaderboardRow> Leaderboard { get; init; } = [];
    /// <summary>Display label for the score source shown on the overlay (e.g. Global / contest name).</summary>
    public string SourceLabel { get; init; } = "Global";
    public bool IsConnected { get; init; }
    public bool IsTimeTrial { get; init; }
}

/// <summary>Centralizes the two supported leaderboard sizes (TOP 5 / TOP 10) to avoid scattering the "is 10 ? 10 : 5" check.</summary>
public static class LeaderboardSizes
{
    public const int Default = 5;
    public const int Extended = 10;

    public static int Normalize(int size) => size == Extended ? Extended : Default;
}
