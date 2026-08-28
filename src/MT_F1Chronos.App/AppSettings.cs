namespace MT_F1Chronos.App;

public sealed class AppSettings
{
    public int UdpPort { get; set; } = 20888;
    public int UdpFormat { get; set; } = 2025;
    public double OverlayTop { get; set; } = 195;
    public double OverlayRight { get; set; } = 12;
    public double OverlayWidth { get; set; } = 300;
    public int LeaderboardSize { get; set; } = 5;
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Contest marked as principal for the overlay (empty = none).
    /// Visibility is controlled by <see cref="ShowContestOnOverlay"/> / <see cref="HideGlobalWhenContest"/>.
    /// </summary>
    public string OverlayContestId { get; set; } = string.Empty;

    /// <summary>
    /// When true and a principal contest is set, show the contest leaderboard on the overlay.
    /// Combined with <see cref="HideGlobalWhenContest"/>:
    /// Global+Contest (false), Contest only (true), Global only (this flag false).
    /// </summary>
    public bool ShowContestOnOverlay { get; set; } = true;

    /// <summary>TOP 3 / 5 / 10 for the contest panel on the overlay.</summary>
    public int ContestLeaderboardSize { get; set; } = 10;

    /// <summary>When true, hide the global TOP while the contest panel is shown.</summary>
    public bool HideGlobalWhenContest { get; set; }

    /// <summary>When true, overlay (and default scores filter) show only each player's best lap.</summary>
    public bool BestPerPlayer { get; set; }

    /// <summary>Optional results server. Off by default — the overlay keeps working fully offline.</summary>
    public bool ResultsServerEnabled { get; set; }

    /// <summary>Base URL of the results server, e.g. <c>https://classement.exemple.com</c> (HTTPS / port 443).</summary>
    public string ResultsServerUrl { get; set; } = string.Empty;

    /// <summary>Shared token sent as <c>X-Results-Token</c>. Empty = no auth.</summary>
    public string ResultsServerToken { get; set; } = string.Empty;

    /// <summary>Stable id for this simulator, generated locally on first run.</summary>
    public string SimulatorId { get; set; } = string.Empty;

    /// <summary>Optional display name of this simulator on the results site.</summary>
    public string SimulatorLabel { get; set; } = string.Empty;

    /// <summary>Seconds between periodic sync pulls (jobs + snapshot). Clamped 15–600.</summary>
    public int ResultsSyncIntervalSeconds { get; set; } = 120;
}

/// <summary>How global / contest leaderboards appear on the overlay.</summary>
public enum OverlayDisplayMode
{
    /// <summary>Global TOP + principal contest TOP.</summary>
    GlobalAndContest = 0,

    /// <summary>Principal contest TOP only.</summary>
    ContestOnly = 1,

    /// <summary>Global TOP only (principal contest kept for later).</summary>
    GlobalOnly = 2,
}
