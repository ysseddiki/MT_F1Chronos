namespace MT_F1Chronos.Core.Telemetry;

/// <summary>
/// Mutable working state for the UDP parser. Publish only via <see cref="Clone"/> —
/// UI and consumers must never mutate a published snapshot.
/// </summary>
public sealed class TelemetryState
{
    public bool IsReceiving { get; set; }
    public DateTime LastPacketUtc { get; set; }
    public ulong SessionUid { get; set; }
    public int TrackId { get; set; } = -1;
    public int RawTrackId { get; set; } = -1;
    public ushort TrackLengthMeters { get; set; }
    public byte SessionType { get; set; }
    public byte GameMode { get; set; }
    public byte PlayerCarIndex { get; set; }
    public byte ResolvedCarIndex { get; set; }
    public byte DriverStatus { get; set; }
    public byte CurrentLapInvalid { get; set; }
    public ushort PacketFormat { get; set; }
    public ushort ConfiguredFormat { get; set; }
    public byte LastPacketId { get; set; }
    public uint? SessionBestLapMs { get; set; }
    public uint? PersonalBestLapMs { get; set; }
    public uint? CurrentLastLapMs { get; set; }
    public uint? CurrentLapTimeMs { get; set; }
    public string? LastEventCode { get; set; }

    public bool IsOnTrack =>
        DriverStatus is 1 or 2 or 4;

    public byte TimeTrialSessionType { get; set; } = 13;

    public bool IsTimeTrial =>
        SessionType == TimeTrialSessionType ||
        GameMode == F1UdpConstants.GameModeTimeTrial;

    public string TrackName => F1UdpConstants.GetTrackName(TrackId);

    public uint? EffectiveBestLapMs =>
        SessionBestLapMs ?? PersonalBestLapMs ?? CurrentLastLapMs;

    public void ResetLapData()
    {
        SessionBestLapMs = null;
        PersonalBestLapMs = null;
        CurrentLastLapMs = null;
        CurrentLapTimeMs = null;
        DriverStatus = 0;
        CurrentLapInvalid = 0;
    }

    /// <summary>Deep-enough copy for safe cross-thread publication.</summary>
    public TelemetryState Clone() => new()
    {
        IsReceiving = IsReceiving,
        LastPacketUtc = LastPacketUtc,
        SessionUid = SessionUid,
        TrackId = TrackId,
        RawTrackId = RawTrackId,
        TrackLengthMeters = TrackLengthMeters,
        SessionType = SessionType,
        GameMode = GameMode,
        PlayerCarIndex = PlayerCarIndex,
        ResolvedCarIndex = ResolvedCarIndex,
        DriverStatus = DriverStatus,
        CurrentLapInvalid = CurrentLapInvalid,
        PacketFormat = PacketFormat,
        ConfiguredFormat = ConfiguredFormat,
        LastPacketId = LastPacketId,
        SessionBestLapMs = SessionBestLapMs,
        PersonalBestLapMs = PersonalBestLapMs,
        CurrentLastLapMs = CurrentLastLapMs,
        CurrentLapTimeMs = CurrentLapTimeMs,
        LastEventCode = LastEventCode,
        TimeTrialSessionType = TimeTrialSessionType,
    };
}

public sealed class TelemetryUpdate
{
    public required TelemetryState State { get; init; }
    public bool SessionStarted { get; init; }
    public bool SessionEnded { get; init; }
    public bool TrackChanged { get; init; }
    public bool LapCompleted { get; init; }
    public uint? CompletedLapMs { get; init; }
}
