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
    public bool ShowGlobalLeaderboard { get; init; } = true;
    public bool ShowContestLeaderboard { get; init; }
    public string ContestLabel { get; init; } = string.Empty;
    public int ContestLeaderboardSize { get; init; } = LeaderboardSizes.Extended;
    public IReadOnlyList<LeaderboardRow> ContestLeaderboard { get; init; } = [];
    public bool IsConnected { get; init; }
    public bool IsTimeTrial { get; init; }
}

/// <summary>Supported overlay leaderboard sizes (TOP 3 / 5 / 10).</summary>
public static class LeaderboardSizes
{
    public const int Compact = 3;
    public const int Default = 5;
    public const int Extended = 10;

    public static int Normalize(int size) => size switch
    {
        Compact => Compact,
        Extended => Extended,
        _ => Default,
    };

    public static string FormatLabel(int size) => $"TOP {Normalize(size)}";
}
