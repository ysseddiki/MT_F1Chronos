namespace MT_F1Chronos.Core.Telemetry;

public sealed class PacketLogEntry
{
    public DateTime Timestamp { get; init; }
    public byte PacketId { get; init; }
    public int BufferLength { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed class CarLapDebugRow
{
    public int CarIndex { get; init; }
    public uint LastLapMs { get; init; }
    public uint CurrentLapMs { get; init; }
    public byte DriverStatus { get; init; }
    public bool IsPlayerCar { get; init; }
    public bool IsResolvedCar { get; init; }
}

public sealed class SessionStoreDebugInfo
{
    public bool HasActiveSession { get; init; }
    public string? ActiveSessionName { get; init; }
    public int? ActiveTrackId { get; init; }
    public string? ActiveTrackName { get; init; }
    public uint? ActiveBestLapMs { get; init; }
    public string SessionsFilePath { get; init; } = string.Empty;
    public int TotalSessions { get; init; }
    public int ScoredSessions { get; init; }
}

public sealed class TelemetryDebugSnapshot
{
    public ushort ConfiguredFormat { get; init; }
    public ushort ReceivedFormat { get; init; }
    public int PacketsPerSecond { get; init; }
    public bool IsConnected { get; init; }
    public double SecondsSinceLastPacket { get; init; }
    public IReadOnlyDictionary<byte, int> PacketCounts { get; init; } = new Dictionary<byte, int>();

    public int RawTrackId { get; init; }
    public int ResolvedTrackId { get; init; }
    public ushort TrackLengthMeters { get; init; }
    public byte SessionType { get; init; }
    public byte GameMode { get; init; }
    public ulong SessionUid { get; init; }
    public bool IsTimeTrial { get; init; }

    public byte PlayerCarIndex { get; init; }
    public byte ResolvedCarIndex { get; init; }
    public uint RawLastLapMs { get; init; }
    public uint RawCurrentLapMs { get; init; }
    public byte DriverStatus { get; init; }
    public int LapDataOffset { get; init; }
    public int LapDataSize { get; init; }
    public IReadOnlyList<CarLapDebugRow> Cars { get; init; } = [];

    public uint TimeTrialSessionBestMs { get; init; }
    public uint TimeTrialPersonalBestMs { get; init; }

    public string LastEventCode { get; init; } = "—";
    public bool LastLapCompleted { get; init; }
    public uint? LastCompletedLapMs { get; init; }
    public bool LastSessionStarted { get; init; }
    public bool LastSessionEnded { get; init; }
    public bool LastTrackChanged { get; init; }

    public uint? ParsedSessionBestMs { get; init; }
    public uint? ParsedPersonalBestMs { get; init; }
    public uint? ParsedCurrentLastLapMs { get; init; }
    public uint? ParsedCurrentLapMs { get; init; }

    public SessionStoreDebugInfo Store { get; init; } = new();
    public IReadOnlyList<PacketLogEntry> PacketLog { get; init; } = [];
}
