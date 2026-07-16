namespace MT_F1Chronos.App;

public sealed class AppSettings
{
    /// <summary>Overlay opacity is clamped to this range everywhere it's read or set.</summary>
    public const double MinOpacity = 0.6;
    public const double MaxOpacity = 1.0;

    public int UdpPort { get; set; } = 20777;
    public int UdpFormat { get; set; } = 2025;
    public double OverlayTop { get; set; } = 195;
    public double OverlayRight { get; set; } = 12;
    public double OverlayWidth { get; set; } = 288;
    public double OverlayOpacity { get; set; } = 0.96;
    public int LeaderboardSize { get; set; } = 5;
    public string PlayerName { get; set; } = string.Empty;
}
