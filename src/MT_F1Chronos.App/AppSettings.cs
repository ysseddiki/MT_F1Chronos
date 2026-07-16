namespace MT_F1Chronos.App;

public sealed class AppSettings
{
    public int UdpPort { get; set; } = 20777;
    public int UdpFormat { get; set; } = 2025;
    public double OverlayTop { get; set; } = 195;
    public double OverlayRight { get; set; } = 12;
    public double OverlayWidth { get; set; } = 288;
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// When true, shows score reset actions in the burger menu (password required).
    /// Enable via settings.json: "enableScoreReset": true
    /// </summary>
    public bool EnableScoreReset { get; set; }
}
