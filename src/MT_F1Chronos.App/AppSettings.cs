namespace MT_F1Chronos.App;

public sealed class AppSettings
{
    public int UdpPort { get; set; } = 20777;
    public int UdpFormat { get; set; } = 2025;
    public double OverlayTop { get; set; } = 195;
    public double OverlayRight { get; set; } = 12;
    public double OverlayWidth { get; set; } = 268;
    public string PlayerName { get; set; } = string.Empty;
}
