using MT_F1Chronos.Core.Telemetry;
using Xunit;

namespace MT_F1Chronos.Tests.Telemetry;

public class F1UdpPacketParserTests
{
    [Fact]
    public void ValidLap_AfterSeed_SetsLapCompleted()
    {
        var parser = new F1UdpPacketParser();
        parser.SetFormat(2025);
        var state = new TelemetryState();

        Assert.True(parser.TryParse(UdpPacketBuilder.SessionPacket(trackId: 7), state, out _));
        Assert.Equal(7, state.TrackId);

        // BR-02: first lastLap after format/session seed is adopted without recording.
        Assert.True(parser.TryParse(
            UdpPacketBuilder.LapDataPacket(lastLapMs: 90_000, currentLapMs: 1_000),
            state, out var seeded));
        Assert.NotNull(seeded);
        Assert.False(seeded!.LapCompleted);
        Assert.Equal(90_000u, state.CurrentLastLapMs);

        // New lastLap with previous invalid == 0 → BR-01 complete.
        Assert.True(parser.TryParse(
            UdpPacketBuilder.LapDataPacket(lastLapMs: 85_000, currentLapMs: 500),
            state, out var completed));
        Assert.NotNull(completed);
        Assert.True(completed!.LapCompleted);
        Assert.Equal(85_000u, completed.CompletedLapMs);
    }

    [Fact]
    public void InvalidPreviousLap_DoesNotComplete()
    {
        var parser = new F1UdpPacketParser();
        parser.SetFormat(2025);
        var state = new TelemetryState();

        parser.TryParse(UdpPacketBuilder.SessionPacket(7), state, out _);
        parser.TryParse(
            UdpPacketBuilder.LapDataPacket(90_000, 1_000, currentLapInvalid: 1),
            state, out _);

        Assert.True(parser.TryParse(
            UdpPacketBuilder.LapDataPacket(80_000, 500, currentLapInvalid: 0),
            state, out var update));
        Assert.False(update!.LapCompleted);
    }

    [Fact]
    public void SessionUidChange_ReseedsWithoutRecordingBroadcastLastLap()
    {
        var parser = new F1UdpPacketParser();
        parser.SetFormat(2025);
        var state = new TelemetryState();

        parser.TryParse(UdpPacketBuilder.SessionPacket(7, sessionUid: 1), state, out _);
        parser.TryParse(UdpPacketBuilder.LapDataPacket(90_000, 1_000, sessionUid: 1), state, out _);
        parser.TryParse(UdpPacketBuilder.LapDataPacket(85_000, 500, sessionUid: 1), state, out var first);
        Assert.True(first!.LapCompleted);

        Assert.True(parser.TryParse(
            UdpPacketBuilder.LapDataPacket(84_000, 100, sessionUid: 2),
            state, out var afterUid));
        Assert.True(afterUid!.SessionStarted);
        Assert.False(afterUid.LapCompleted);
        Assert.Equal(84_000u, state.CurrentLastLapMs);
    }

    [Fact]
    public void EventSsta_ReseedsLapContext()
    {
        var parser = new F1UdpPacketParser();
        parser.SetFormat(2025);
        var state = new TelemetryState { SessionUid = 1 };

        parser.TryParse(UdpPacketBuilder.LapDataPacket(90_000, 1_000), state, out _);
        parser.TryParse(UdpPacketBuilder.LapDataPacket(85_000, 500), state, out var completed);
        Assert.True(completed!.LapCompleted);

        Assert.True(parser.TryParse(UdpPacketBuilder.EventPacket("SSTA"), state, out var ssta));
        Assert.True(ssta!.SessionStarted);
        Assert.Null(state.CurrentLastLapMs);

        Assert.True(parser.TryParse(
            UdpPacketBuilder.LapDataPacket(91_000, 200),
            state, out var seeded));
        Assert.False(seeded!.LapCompleted);
    }

    [Fact]
    public void EventSend_SetsSessionEnded()
    {
        var parser = new F1UdpPacketParser();
        parser.SetFormat(2025);
        var state = new TelemetryState { SessionUid = 1 };

        Assert.True(parser.TryParse(UdpPacketBuilder.EventPacket("SEND"), state, out var update));
        Assert.True(update!.SessionEnded);
        Assert.Equal("SEND", state.LastEventCode);
    }

    [Fact]
    public void GhostMelbourne_RejectedWhileOnTrack()
    {
        var parser = new F1UdpPacketParser();
        parser.SetFormat(2025);
        var state = new TelemetryState();

        parser.TryParse(UdpPacketBuilder.SessionPacket(trackId: 7), state, out _);
        Assert.Equal(7, state.TrackId);

        parser.TryParse(
            UdpPacketBuilder.LapDataPacket(0, currentLapMs: 12_000, driverStatus: 1),
            state, out _);

        parser.TryParse(UdpPacketBuilder.SessionPacket(trackId: 0), state, out var ghost);
        Assert.Equal(7, state.TrackId);
        Assert.False(ghost!.TrackChanged);
    }

    [Fact]
    public void ZeroLastLapMs_DoesNotComplete()
    {
        var parser = new F1UdpPacketParser();
        parser.SetFormat(2025);
        var state = new TelemetryState();

        parser.TryParse(UdpPacketBuilder.SessionPacket(7), state, out _);
        parser.TryParse(UdpPacketBuilder.LapDataPacket(90_000, 1_000), state, out _);

        Assert.True(parser.TryParse(
            UdpPacketBuilder.LapDataPacket(0, 2_000),
            state, out var update));
        Assert.False(update!.LapCompleted);
    }
}
