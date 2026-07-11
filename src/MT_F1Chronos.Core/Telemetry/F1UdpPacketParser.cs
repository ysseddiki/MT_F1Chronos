using System.Buffers.Binary;
using System.Text;

namespace MT_F1Chronos.Core.Telemetry;

public sealed class F1UdpPacketParser
{
    private int _packetCount;
    private DateTime _packetCountWindowStart = DateTime.UtcNow;
    private byte _lastLapDataPacketId;

    public UdpFormatProfile Profile { get; private set; } = UdpFormatProfile.Format2025;

    public int PacketsPerSecond
    {
        get
        {
            var elapsed = (DateTime.UtcNow - _packetCountWindowStart).TotalSeconds;
            return elapsed < 1 ? _packetCount : (int)(_packetCount / elapsed);
        }
    }

    public void SetFormat(ushort format)
    {
        Profile = UdpFormatProfile.For(format);
    }

    public bool TryParse(ReadOnlySpan<byte> buffer, TelemetryState state, out TelemetryUpdate? update)
    {
        update = null;
        if (buffer.Length < F1UdpConstants.HeaderSize)
            return false;

        TrackPacketRate();

        state.PacketFormat = BinaryPrimitives.ReadUInt16LittleEndian(buffer[0..2]);
        state.ConfiguredFormat = Profile.Format;
        var packetId = buffer[5];
        var sessionUid = BinaryPrimitives.ReadUInt64LittleEndian(buffer[6..14]);
        state.PlayerCarIndex = buffer[22];
        state.LastPacketId = packetId;
        state.IsReceiving = true;
        state.LastPacketUtc = DateTime.UtcNow;

        if (packetId == F1UdpConstants.PacketLapData)
            _lastLapDataPacketId = packetId;

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

    public TelemetryDiagnostics BuildDiagnostics(TelemetryState state) =>
        new()
        {
            ConfiguredFormat = state.ConfiguredFormat,
            PacketFormat = state.PacketFormat,
            LastPacketId = state.LastPacketId,
            LastLapDataPacketId = _lastLapDataPacketId,
            PlayerCarIndex = state.PlayerCarIndex,
            ResolvedCarIndex = state.ResolvedCarIndex,
            TrackId = state.TrackId,
            TrackLengthMeters = state.TrackLengthMeters,
            DriverStatus = state.DriverStatus,
            LastLapMs = state.CurrentLastLapMs ?? 0,
            CurrentLapMs = state.CurrentLapTimeMs ?? 0,
            SessionBestMs = state.SessionBestLapMs ?? 0,
            PacketsPerSecond = PacketsPerSecond,
            LastEventCode = state.LastEventCode ?? "—",
        };

    private void TrackPacketRate()
    {
        _packetCount++;
        var elapsed = (DateTime.UtcNow - _packetCountWindowStart).TotalSeconds;
        if (elapsed >= 1)
        {
            _packetCount = 1;
            _packetCountWindowStart = DateTime.UtcNow;
        }
    }

    private void ParseSessionPacket(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        var offset = F1UdpConstants.HeaderSize;

        if (buffer.Length < offset + 8)
            return;

        offset += 1; // weather
        offset += 1; // track temperature
        offset += 1; // air temperature
        offset += 1; // total laps
        state.TrackLengthMeters = BinaryPrimitives.ReadUInt16LittleEndian(buffer[offset..]);
        offset += 2;

        state.SessionType = buffer[offset++];
        var rawTrackId = unchecked((sbyte)buffer[offset++]);
        var trackId = F1UdpConstants.ResolveTrackId(rawTrackId, state.TrackLengthMeters);

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
        offset += F1UdpConstants.WeatherForecastSampleCount * Profile.WeatherForecastSampleSize;
        offset += 1; // forecast accuracy
        offset += 1; // ai difficulty
        offset += 12; // season / weekend / session link identifiers
        offset += 3; // pit stop window fields
        offset += 9; // driver assists through dynamic racing line type

        if (buffer.Length > offset)
            state.GameMode = buffer[offset];
    }

    private bool ShouldAcceptTrackUpdate(TelemetryState state, int newTrackId)
    {
        if (newTrackId < 0)
            return false;

        if (state.TrackId < 0)
            return true;

        if (newTrackId == state.TrackId)
            return true;

        var isDriving = state.IsOnTrack ||
                        state.CurrentLapTimeMs is > 0;

        if (isDriving && state.TrackId > 0 && newTrackId == 0)
            return false;

        return true;
    }

    private void ParseLapDataPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref bool lapCompleted,
        ref uint? completedLapMs)
    {
        var carIndex = ResolveActiveCarIndex(buffer, state);
        state.ResolvedCarIndex = carIndex;

        var offset = F1UdpConstants.HeaderSize + carIndex * Profile.LapDataSize;
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

    private byte ResolveActiveCarIndex(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        byte? bestByCurrentLap = null;
        uint bestCurrentLap = 0;

        for (byte i = 0; i < Profile.MaxCars; i++)
        {
            var currentLap = ReadCurrentLap(buffer, i);
            if (currentLap > bestCurrentLap)
            {
                bestCurrentLap = currentLap;
                bestByCurrentLap = i;
            }
        }

        if (bestByCurrentLap.HasValue)
            return bestByCurrentLap.Value;

        if (state.PlayerCarIndex < Profile.MaxCars &&
            IsActiveDriver(ReadDriverStatus(buffer, state.PlayerCarIndex)))
            return state.PlayerCarIndex;

        for (byte i = 0; i < Profile.MaxCars; i++)
        {
            if (IsActiveDriver(ReadDriverStatus(buffer, i)))
                return i;
        }

        if (state.PlayerCarIndex < Profile.MaxCars)
            return state.PlayerCarIndex;

        return 0;
    }

    private uint ReadCurrentLap(ReadOnlySpan<byte> buffer, int carIndex)
    {
        var offset = F1UdpConstants.HeaderSize + carIndex * Profile.LapDataSize + 4;
        if (buffer.Length < offset + 4)
            return 0;

        return BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
    }

    private byte ReadDriverStatus(ReadOnlySpan<byte> buffer, int carIndex)
    {
        var offset = F1UdpConstants.HeaderSize +
                     carIndex * Profile.LapDataSize +
                     F1UdpConstants.LapDataDriverStatusOffset;

        return buffer.Length > offset ? buffer[offset] : (byte)0;
    }

    private static bool IsActiveDriver(byte driverStatus) =>
        driverStatus is 1 or 2 or 3 or 4;

    private void ParseEventPacket(
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

    private void ParseTimeTrialPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref uint? completedLapMs)
    {
        var offset = F1UdpConstants.HeaderSize;

        if (buffer.Length < offset + Profile.TimeTrialDataSetSize)
            return;

        var sessionBest = ReadTimeTrialLapTime(buffer, offset);
        if (sessionBest > 0)
        {
            state.SessionBestLapMs = sessionBest;
            completedLapMs ??= sessionBest;
        }

        offset += Profile.TimeTrialDataSetSize;
        if (buffer.Length < offset + Profile.TimeTrialDataSetSize)
            return;

        var personalBest = ReadTimeTrialLapTime(buffer, offset);
        if (personalBest > 0)
            state.PersonalBestLapMs = personalBest;
    }

    private void ParseSessionHistoryPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref uint? completedLapMs)
    {
        if (buffer.Length < F1UdpConstants.SessionHistoryLapDataOffset + 4)
            return;

        var carIdx = buffer[F1UdpConstants.HeaderSize];
        if (carIdx >= Profile.MaxCars)
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

    private uint ReadTimeTrialLapTime(ReadOnlySpan<byte> buffer, int dataSetOffset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[(dataSetOffset + Profile.TimeTrialLapTimeOffset)..]);
}
