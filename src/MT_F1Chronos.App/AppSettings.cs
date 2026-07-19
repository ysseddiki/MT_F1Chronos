namespace MT_F1Chronos.App;

public sealed class AppSettings
{
    public int UdpPort { get; set; } = 20888;
    public int UdpFormat { get; set; } = 2025;
    public double OverlayTop { get; set; } = 195;
    public double OverlayRight { get; set; } = 12;
    public double OverlayWidth { get; set; } = 288;
    public int LeaderboardSize { get; set; } = 5;
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>Empty = no contest panel. Otherwise the contest id for the secondary TOP 10.</summary>
    public string OverlayContestId { get; set; } = string.Empty;

    /// <summary>When true and a contest is selected, show contest TOP 10 under the global leaderboard.</summary>
    public bool ShowContestOnOverlay { get; set; } = true;
}
