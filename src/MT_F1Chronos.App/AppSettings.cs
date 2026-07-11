namespace MT_F1Chronos.App;

public sealed class AppSettings
{
    public int UdpPort { get; set; } = 20777;

    /// <summary>Distance from top of screen (px) — aligned below F1 timing panel.</summary>
    public double OverlayTop { get; set; } = 195;

    /// <summary>Distance from right edge (px).</summary>
    public double OverlayRight { get; set; } = 12;

    public double OverlayWidth { get; set; } = 268;

    public string PlayerName { get; set; } = string.Empty;
}
