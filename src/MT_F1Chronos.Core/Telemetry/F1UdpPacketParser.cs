using System.Buffers.Binary;
using System.Text;

namespace MT_F1Chronos.Core.Telemetry;

public static class F1UdpPacketParser
{
    public static bool TryParse(ReadOnlySpan<byte> buffer, TelemetryState state, out TelemetryUpdate? update)
    {
        update = null;
        if (buffer.Length < F1UdpConstants.HeaderSize)
            return false;

        var packetId = buffer[5];
        var sessionUid = BinaryPrimitives.ReadUInt64LittleEndian(buffer[6..14]);
        state.PlayerCarIndex = buffer[22];
        state.IsReceiving = true;
        state.LastPacketUtc = DateTime.UtcNow;

        var sessionChanged = state.SessionUid != 0 && sessionUid != state.SessionUid;
        if (sessionChanged)
            state.ResetForNewSession();

        var previousTrackId = state.TrackId;
        state.SessionUid = sessionUid;

        var sessionStarted = false;
        var sessionEnded = false;
        var lapCompleted = false;
        uint? completedLapMs = null;

        switch (packetId)
        {
            case F1UdpConstants.PacketSession:
                ParseSessionPacket(buffer, state);
                break;

            case F1UdpConstants.PacketLapData:
                ParseLapDataPacket(buffer, state, ref lapCompleted, ref completedLapMs);
                break;

            case F1UdpConstants.PacketEvent:
                ParseEventPacket(buffer, state, ref sessionStarted, ref sessionEnded);
                break;

            case F1UdpConstants.PacketTimeTrial:
                ParseTimeTrialPacket(buffer, state);
                break;
        }

        if (sessionChanged)
            sessionStarted = true;

        var trackChanged = previousTrackId != state.TrackId && state.TrackId >= 0;

        update = new TelemetryUpdate
        {
            State = state,
            SessionStarted = sessionStarted,
            SessionEnded = sessionEnded,
            TrackChanged = trackChanged,
            LapCompleted = lapCompleted,
            CompletedLapMs = completedLapMs,
        };

        return true;
    }

    private static void ParseSessionPacket(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        var offset = F1UdpConstants.HeaderSize;

        if (buffer.Length < offset + 8)
            return;

        offset += 1; // weather
        offset += 1; // track temperature
        offset += 1; // air temperature
        offset += 1; // total laps
        offset += 2; // track length

        state.SessionType = buffer[offset++];
        state.TrackId = unchecked((sbyte)buffer[offset++]);
        offset += 1; // formula
        offset += 2; // session time left
        offset += 2; // session duration
        offset += 1; // pit speed limit
        offset += 1; // game paused
        offset += 1; // is spectating
        offset += 1; // spectator car index
        offset += 1; // sli pro support
        offset += 1; // num marshal zones
        offset += F1UdpConstants.MarshalZoneCount * F1UdpConstants.MarshalZoneSize;
        offset += 1; // safety car status
        offset += 1; // network game
        offset += 1; // num weather forecast samples
        offset += F1UdpConstants.WeatherForecastSampleCount * F1UdpConstants.WeatherForecastSampleSize;
        offset += 1; // forecast accuracy
        offset += 1; // ai difficulty
        offset += 12; // season / weekend / session link identifiers
        offset += 3; // pit stop window fields
        offset += 9; // driver assists through dynamic racing line type

        if (buffer.Length > offset)
            state.GameMode = buffer[offset];
    }

    private static void ParseLapDataPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref bool lapCompleted,
        ref uint? completedLapMs)
    {
        var playerIndex = state.PlayerCarIndex;
        if (playerIndex >= F1UdpConstants.MaxCars)
            return;

        var offset = F1UdpConstants.HeaderSize + playerIndex * F1UdpConstants.LapDataSize;
        if (buffer.Length < offset + 8)
            return;

        var lastLap = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
        var currentLap = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 4)..]);

        if (currentLap > 0)
            state.CurrentLapTimeMs = currentLap;

        if (lastLap == 0)
            return;

        if (state.CurrentLastLapMs != lastLap)
        {
            lapCompleted = state.CurrentLastLapMs.HasValue;
            completedLapMs = lastLap;
            state.CurrentLastLapMs = lastLap;

            if (!state.SessionBestLapMs.HasValue || lastLap < state.SessionBestLapMs)
                state.SessionBestLapMs = lastLap;
        }
    }

    private static void ParseEventPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref bool sessionStarted,
        ref bool sessionEnded)
    {
        if (buffer.Length < F1UdpConstants.HeaderSize + 4)
            return;

        var code = Encoding.ASCII.GetString(buffer[F1UdpConstants.HeaderSize..(F1UdpConstants.HeaderSize + 4)]);
        state.LastEventCode = code;

        sessionStarted = code == "SSTA";
        sessionEnded = code == "SEND";
    }

    private static void ParseTimeTrialPacket(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        var offset = F1UdpConstants.HeaderSize;

        if (buffer.Length < offset + F1UdpConstants.TimeTrialDataSetSize)
            return;

        var sessionBest = ReadTimeTrialLapTime(buffer, offset);
        if (sessionBest > 0)
            state.SessionBestLapMs = sessionBest;

        offset += F1UdpConstants.TimeTrialDataSetSize;
        if (buffer.Length < offset + F1UdpConstants.TimeTrialDataSetSize)
            return;

        var personalBest = ReadTimeTrialLapTime(buffer, offset);
        if (personalBest > 0)
            state.PersonalBestLapMs = personalBest;
    }

    private static uint ReadTimeTrialLapTime(ReadOnlySpan<byte> buffer, int dataSetOffset)
    {
        // carIdx (1) + teamId (2) => lap time starts at +3
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer[(dataSetOffset + 3)..]);
    }
}
