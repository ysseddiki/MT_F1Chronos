using System.Buffers.Binary;
using System.Text;

namespace MT_F1Chronos.Core.Telemetry;

public static class F1UdpPacketParser
{
    private static int _packetCount;
    private static DateTime _packetCountWindowStart = DateTime.UtcNow;

    public static int PacketsPerSecond
    {
        get
        {
            var elapsed = (DateTime.UtcNow - _packetCountWindowStart).TotalSeconds;
            return elapsed < 1 ? _packetCount : (int)(_packetCount / elapsed);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> buffer, TelemetryState state, out TelemetryUpdate? update)
    {
        update = null;
        if (buffer.Length < F1UdpConstants.HeaderSize)
            return false;

        TrackPacketRate();

        state.PacketFormat = BinaryPrimitives.ReadUInt16LittleEndian(buffer[0..2]);
        var packetId = buffer[5];
        var sessionUid = BinaryPrimitives.ReadUInt64LittleEndian(buffer[6..14]);
        state.PlayerCarIndex = buffer[22];
        state.LastPacketId = packetId;
        state.IsReceiving = true;
        state.LastPacketUtc = DateTime.UtcNow;

        var sessionChanged = state.SessionUid != 0 && sessionUid != state.SessionUid;
        if (sessionChanged)
            state.ResetLapData();

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
                ParseTimeTrialPacket(buffer, state, ref completedLapMs);
                break;

            case F1UdpConstants.PacketSessionHistory:
                ParseSessionHistoryPacket(buffer, state, ref completedLapMs);
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

    public static TelemetryDiagnostics BuildDiagnostics(TelemetryState state) =>
        new()
        {
            PacketFormat = state.PacketFormat,
            LastPacketId = state.LastPacketId,
            PlayerCarIndex = state.PlayerCarIndex,
            ResolvedCarIndex = state.ResolvedCarIndex,
            TrackId = state.TrackId,
            DriverStatus = state.DriverStatus,
            LastLapMs = state.CurrentLastLapMs ?? 0,
            CurrentLapMs = state.CurrentLapTimeMs ?? 0,
            SessionBestMs = state.SessionBestLapMs ?? 0,
            PacketsPerSecond = PacketsPerSecond,
            LastEventCode = state.LastEventCode ?? "—",
        };

    private static void TrackPacketRate()
    {
        _packetCount++;
        var elapsed = (DateTime.UtcNow - _packetCountWindowStart).TotalSeconds;
        if (elapsed >= 1)
        {
            _packetCount = 1;
            _packetCountWindowStart = DateTime.UtcNow;
        }
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
        var trackId = unchecked((sbyte)buffer[offset++]);

        if (ShouldAcceptTrackUpdate(state, trackId))
            state.TrackId = trackId;

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

    private static bool ShouldAcceptTrackUpdate(TelemetryState state, int newTrackId)
    {
        if (newTrackId < 0)
            return false;

        if (state.TrackId < 0)
            return true;

        if (newTrackId == state.TrackId)
            return true;

        // Ignore spurious Melbourne (0) from menus while actively driving on another circuit.
        if (state.IsOnTrack && state.TrackId > 0 && newTrackId == 0)
            return false;

        return true;
    }

    private static void ParseLapDataPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref bool lapCompleted,
        ref uint? completedLapMs)
    {
        var carIndex = ResolveActiveCarIndex(buffer, state);
        state.ResolvedCarIndex = carIndex;

        var offset = F1UdpConstants.HeaderSize + carIndex * F1UdpConstants.LapDataSize;
        if (buffer.Length < offset + F1UdpConstants.LapDataDriverStatusOffset + 1)
            return;

        var lastLap = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
        var currentLap = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 4)..]);
        state.DriverStatus = buffer[offset + F1UdpConstants.LapDataDriverStatusOffset];

        if (currentLap > 0)
            state.CurrentLapTimeMs = currentLap;

        if (lastLap == 0)
            return;

        var isNewLap = state.CurrentLastLapMs != lastLap;
        state.CurrentLastLapMs = lastLap;

        if (!state.SessionBestLapMs.HasValue || lastLap < state.SessionBestLapMs)
            state.SessionBestLapMs = lastLap;

        if (isNewLap)
        {
            lapCompleted = true;
            completedLapMs = lastLap;
        }
    }

    private static byte ResolveActiveCarIndex(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        if (state.PlayerCarIndex < F1UdpConstants.MaxCars &&
            IsActiveDriver(ReadDriverStatus(buffer, state.PlayerCarIndex)))
            return state.PlayerCarIndex;

        for (byte i = 0; i < F1UdpConstants.MaxCars; i++)
        {
            if (IsActiveDriver(ReadDriverStatus(buffer, i)))
                return i;
        }

        if (state.PlayerCarIndex < F1UdpConstants.MaxCars)
            return state.PlayerCarIndex;

        return 0;
    }

    private static byte ReadDriverStatus(ReadOnlySpan<byte> buffer, int carIndex)
    {
        var offset = F1UdpConstants.HeaderSize +
                     carIndex * F1UdpConstants.LapDataSize +
                     F1UdpConstants.LapDataDriverStatusOffset;

        return buffer.Length > offset ? buffer[offset] : (byte)0;
    }

    private static bool IsActiveDriver(byte driverStatus) =>
        driverStatus is 1 or 2 or 4;

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

    private static void ParseTimeTrialPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref uint? completedLapMs)
    {
        var offset = F1UdpConstants.HeaderSize;

        if (buffer.Length < offset + F1UdpConstants.TimeTrialDataSetSize)
            return;

        var sessionBest = ReadTimeTrialLapTime(buffer, offset);
        if (sessionBest > 0)
        {
            state.SessionBestLapMs = sessionBest;
            completedLapMs ??= sessionBest;
        }

        offset += F1UdpConstants.TimeTrialDataSetSize;
        if (buffer.Length < offset + F1UdpConstants.TimeTrialDataSetSize)
            return;

        var personalBest = ReadTimeTrialLapTime(buffer, offset);
        if (personalBest > 0)
            state.PersonalBestLapMs = personalBest;
    }

    private static void ParseSessionHistoryPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref uint? completedLapMs)
    {
        if (buffer.Length < F1UdpConstants.SessionHistoryLapDataOffset + 4)
            return;

        var carIdx = buffer[F1UdpConstants.HeaderSize];
        if (carIdx >= F1UdpConstants.MaxCars)
            return;

        if (carIdx != state.ResolvedCarIndex &&
            carIdx != state.PlayerCarIndex &&
            state.ResolvedCarIndex != 0)
            return;

        var numLaps = buffer[F1UdpConstants.HeaderSize + 1];
        if (numLaps == 0)
            return;

        uint? bestLap = null;

        for (var i = 0; i < numLaps && i < 100; i++)
        {
            var offset = F1UdpConstants.SessionHistoryLapDataOffset +
                         i * F1UdpConstants.LapHistoryDataSize;

            if (buffer.Length < offset + 4)
                break;

            var lapMs = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
            if (lapMs == 0)
                continue;

            if (!bestLap.HasValue || lapMs < bestLap)
                bestLap = lapMs;
        }

        if (!bestLap.HasValue)
            return;

        state.SessionBestLapMs = bestLap;
        state.CurrentLastLapMs ??= bestLap;
        completedLapMs ??= bestLap;
    }

    private static uint ReadTimeTrialLapTime(ReadOnlySpan<byte> buffer, int dataSetOffset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[(dataSetOffset + 3)..]);
}
