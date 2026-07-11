namespace MT_F1Chronos.Core.Telemetry;

public sealed class TelemetryDiagnostics
{
    public ushort PacketFormat { get; init; }
    public byte LastPacketId { get; init; }
    public byte PlayerCarIndex { get; init; }
    public byte ResolvedCarIndex { get; init; }
    public int TrackId { get; init; }
    public byte DriverStatus { get; init; }
    public uint LastLapMs { get; init; }
    public uint CurrentLapMs { get; init; }
    public uint SessionBestMs { get; init; }
    public int PacketsPerSecond { get; init; }
    public string LastEventCode { get; init; } = "—";

    public string ToStatusLine() =>
        $"UDP {PacketFormat} · pkt {LastPacketId} · car {ResolvedCarIndex} · trk {TrackId} · " +
        $"lap {FormatMs(CurrentLapMs)} / best {FormatMs(SessionBestMs)} · drv {DriverStatus} · {PacketsPerSecond} pkt/s";

    private static string FormatMs(uint ms)
    {
        if (ms == 0)
            return "—";

        var totalSeconds = ms / 1000.0;
        var minutes = (int)(totalSeconds / 60);
        var seconds = totalSeconds - minutes * 60;
        return minutes > 0
            ? $"{minutes}:{seconds:00.000}"
            : $"{seconds:0.000}";
    }
}
