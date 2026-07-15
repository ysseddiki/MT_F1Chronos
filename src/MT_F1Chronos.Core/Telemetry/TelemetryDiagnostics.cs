using MT_F1Chronos.Core.Services;

namespace MT_F1Chronos.Core.Telemetry;

public sealed class TelemetryDiagnostics
{
    public ushort ConfiguredFormat { get; init; }
    public ushort PacketFormat { get; init; }
    public byte LastPacketId { get; init; }
    public byte LastLapDataPacketId { get; init; }
    public byte ResolvedCarIndex { get; init; }
    public int TrackId { get; init; }
    public ushort TrackLengthMeters { get; init; }
    public byte DriverStatus { get; init; }
    public uint CurrentLapMs { get; init; }
    public uint SessionBestMs { get; init; }
    public int PacketsPerSecond { get; init; }

    public string ToStatusLine()
    {
        var lap = CurrentLapMs == 0 ? "—" : LapTimeFormatter.Format(CurrentLapMs);
        var best = SessionBestMs == 0 ? "—" : LapTimeFormatter.Format(SessionBestMs);

        return $"cfg {ConfiguredFormat} · rx {PacketFormat} · pkt {LastPacketId} · lapPkt {LastLapDataPacketId} · " +
               $"car {ResolvedCarIndex} · trk {TrackId} ({F1UdpConstants.GetTrackName(TrackId)}) · len {TrackLengthMeters}m · " +
               $"lap {lap} / best {best} · drv {DriverStatus} · {PacketsPerSecond} pkt/s";
    }
}
