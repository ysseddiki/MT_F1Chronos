namespace MT_F1Chronos.Core.Telemetry;

public sealed class TelemetryState
{
    public bool IsReceiving { get; set; }
    public DateTime LastPacketUtc { get; set; }
    public ulong SessionUid { get; set; }
    public int TrackId { get; set; } = -1;
    public byte SessionType { get; set; }
    public byte GameMode { get; set; }
    public byte PlayerCarIndex { get; set; }
    public uint? SessionBestLapMs { get; set; }
    public uint? PersonalBestLapMs { get; set; }
    public uint? CurrentLastLapMs { get; set; }
    public uint? CurrentLapTimeMs { get; set; }
    public string? LastEventCode { get; set; }

    public bool IsTimeTrial =>
        SessionType == F1UdpConstants.SessionTypeTimeTrial ||
        GameMode == F1UdpConstants.GameModeTimeTrial;

    public string TrackName => F1UdpConstants.GetTrackName(TrackId);

    public uint? EffectiveBestLapMs =>
        SessionBestLapMs ?? PersonalBestLapMs ?? CurrentLastLapMs;

    public void ResetForNewSession()
    {
        SessionBestLapMs = null;
        PersonalBestLapMs = null;
        CurrentLastLapMs = null;
        CurrentLapTimeMs = null;
        TrackId = -1;
        SessionType = 0;
        GameMode = 0;
    }
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
