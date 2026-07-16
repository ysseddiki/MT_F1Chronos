using System.Buffers.Binary;
using System.Text;

namespace MT_F1Chronos.Core.Telemetry;

public sealed class F1UdpPacketParser
{
    private const int PacketLogCapacity = 20;

    private int _packetCount;
    private DateTime _packetCountWindowStart = DateTime.UtcNow;
    private byte _lastLapDataPacketId;

    private readonly Dictionary<byte, int> _packetCounts = new();
    private readonly PacketLogEntry[] _packetLog = new PacketLogEntry[PacketLogCapacity];
    private int _packetLogCount;
    private int _packetLogHead;

    private int _rawTrackId = -1;
    private uint _rawLastLapMs;
    private uint _rawCurrentLapMs;
    private uint _timeTrialSessionBestMs;
    private uint _timeTrialPersonalBestMs;
    private byte _previousCurrentLapInvalid;
    private bool _seedLastLapWithoutRecording;
    private CarLapDebugRow[] _carRows = [];
    private TelemetryUpdate? _lastUpdate;

    public UdpFormatProfile Profile { get; private set; } = UdpFormatProfile.Format2025;

    /// <summary>When true, builds per-car lap rows for the Debug window (extra allocations).</summary>
    public bool CaptureVerboseDebug { get; set; }

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
        _seedLastLapWithoutRecording = true;
    }

    public bool TryParse(ReadOnlySpan<byte> buffer, TelemetryState state, out TelemetryUpdate? update)
    {
        update = null;
        if (buffer.Length < Profile.HeaderSize)
            return false;

        TrackPacketRate();

        state.PacketFormat = BinaryPrimitives.ReadUInt16LittleEndian(buffer[0..2]);
        state.ConfiguredFormat = Profile.Format;
        state.TimeTrialSessionType = Profile.SessionTypeTimeTrial;

        var packetId = buffer[Profile.PacketIdOffset];
        var sessionUid = BinaryPrimitives.ReadUInt64LittleEndian(
            buffer[Profile.SessionUidOffset..(Profile.SessionUidOffset + 8)]);
        state.PlayerCarIndex = buffer[Profile.PlayerCarIndexOffset];
        state.LastPacketId = packetId;
        state.IsReceiving = true;
        state.LastPacketUtc = DateTime.UtcNow;

        IncrementPacketCount(packetId);

        if (packetId == F1UdpConstants.PacketLapData)
            _lastLapDataPacketId = packetId;

        var sessionChanged = state.SessionUid != 0 && sessionUid != state.SessionUid;
        if (sessionChanged)
            BeginNewSession(state);

        var previousTrackId = state.TrackId;
        state.SessionUid = sessionUid;

        var sessionStarted = false;
        var sessionEnded = false;
        var lapCompleted = false;
        uint? completedLapMs = null;
        var packetSummary = $"pkt={packetId} len={buffer.Length}";

        switch (packetId)
        {
            case F1UdpConstants.PacketSession:
                ParseSessionPacket(buffer, state);
                packetSummary = $"session rawTrk={_rawTrackId} trk={state.TrackId} len={state.TrackLengthMeters}m type={state.SessionType} mode={state.GameMode}";
                break;

            case F1UdpConstants.PacketLapData:
                ParseLapDataPacket(buffer, state, ref lapCompleted, ref completedLapMs);
                packetSummary = $"lap player={state.PlayerCarIndex} last={_rawLastLapMs} cur={_rawCurrentLapMs} drv={state.DriverStatus} inv={state.CurrentLapInvalid}";
                break;

            case F1UdpConstants.PacketEvent:
                ParseEventPacket(buffer, state, ref sessionStarted, ref sessionEnded);
                packetSummary = $"event {state.LastEventCode}";
                break;

            case F1UdpConstants.PacketTimeTrial:
                ParseTimeTrialPacket(buffer, state);
                packetSummary = $"tt best={_timeTrialSessionBestMs} personal={_timeTrialPersonalBestMs}";
                break;

            case F1UdpConstants.PacketSessionHistory:
                ParseSessionHistoryPacket(buffer, state);
                packetSummary = $"history car={buffer[Profile.HeaderSize]} laps={buffer[Profile.HeaderSize + 1]}";
                break;

            default:
                packetSummary = $"unknown pkt={packetId}";
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

        _lastUpdate = update;
        AppendPacketLog(packetId, buffer.Length, packetSummary);

        return true;
    }

    public TelemetryDiagnostics BuildDiagnostics(TelemetryState state) =>
        new()
        {
            ConfiguredFormat = state.ConfiguredFormat,
            PacketFormat = state.PacketFormat,
            LastPacketId = state.LastPacketId,
            LastLapDataPacketId = _lastLapDataPacketId,
            ResolvedCarIndex = state.ResolvedCarIndex,
            TrackId = state.TrackId,
            TrackLengthMeters = state.TrackLengthMeters,
            DriverStatus = state.DriverStatus,
            CurrentLapMs = state.CurrentLapTimeMs ?? 0,
            SessionBestMs = state.SessionBestLapMs ?? 0,
            PacketsPerSecond = PacketsPerSecond,
        };

    public TelemetryDebugSnapshot BuildDebugSnapshot(TelemetryState state, SessionStoreDebugInfo storeInfo)
    {
        var lapOffset = Profile.HeaderSize + state.ResolvedCarIndex * Profile.LapDataSize;
        var secondsSince = (DateTime.UtcNow - state.LastPacketUtc).TotalSeconds;

        return new TelemetryDebugSnapshot
        {
            ConfiguredFormat = state.ConfiguredFormat,
            ReceivedFormat = state.PacketFormat,
            PacketsPerSecond = PacketsPerSecond,
            IsConnected = state.IsReceiving && secondsSince < 3,
            SecondsSinceLastPacket = secondsSince,
            PacketCounts = new Dictionary<byte, int>(_packetCounts),

            RawTrackId = _rawTrackId,
            ResolvedTrackId = state.TrackId,
            TrackLengthMeters = state.TrackLengthMeters,
            SessionType = state.SessionType,
            GameMode = state.GameMode,
            SessionUid = state.SessionUid,
            IsTimeTrial = state.IsTimeTrial,

            PlayerCarIndex = state.PlayerCarIndex,
            ResolvedCarIndex = state.ResolvedCarIndex,
            RawLastLapMs = _rawLastLapMs,
            RawCurrentLapMs = _rawCurrentLapMs,
            DriverStatus = state.DriverStatus,
            CurrentLapInvalid = state.CurrentLapInvalid,
            LapDataOffset = lapOffset,
            LapDataSize = Profile.LapDataSize,
            Cars = _carRows,

            TimeTrialSessionBestMs = _timeTrialSessionBestMs,
            TimeTrialPersonalBestMs = _timeTrialPersonalBestMs,

            LastEventCode = state.LastEventCode ?? "—",
            LastLapCompleted = _lastUpdate?.LapCompleted ?? false,
            LastCompletedLapMs = _lastUpdate?.CompletedLapMs,
            LastSessionStarted = _lastUpdate?.SessionStarted ?? false,
            LastSessionEnded = _lastUpdate?.SessionEnded ?? false,
            LastTrackChanged = _lastUpdate?.TrackChanged ?? false,

            ParsedSessionBestMs = state.SessionBestLapMs,
            ParsedPersonalBestMs = state.PersonalBestLapMs,
            ParsedCurrentLastLapMs = state.CurrentLastLapMs,
            ParsedCurrentLapMs = state.CurrentLapTimeMs,

            Store = storeInfo,
            PacketLog = GetPacketLog(),
        };
    }

    private void IncrementPacketCount(byte packetId)
    {
        _packetCounts.TryGetValue(packetId, out var count);
        _packetCounts[packetId] = count + 1;
    }

    private void AppendPacketLog(byte packetId, int bufferLength, string summary)
    {
        _packetLog[_packetLogHead] = new PacketLogEntry
        {
            Timestamp = DateTime.Now,
            PacketId = packetId,
            BufferLength = bufferLength,
            Summary = summary,
        };
        _packetLogHead = (_packetLogHead + 1) % PacketLogCapacity;
        if (_packetLogCount < PacketLogCapacity)
            _packetLogCount++;
    }

    private IReadOnlyList<PacketLogEntry> GetPacketLog()
    {
        var entries = new PacketLogEntry[_packetLogCount];
        for (var i = 0; i < _packetLogCount; i++)
        {
            var index = (_packetLogHead - _packetLogCount + i + PacketLogCapacity) % PacketLogCapacity;
            entries[i] = _packetLog[index];
        }

        return entries.Reverse().ToArray();
    }

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
        var offset = Profile.HeaderSize;

        if (buffer.Length < offset + 8)
            return;

        offset += 1; // weather
        offset += 1; // track temperature
        offset += 1; // air temperature
        offset += 1; // total laps
        state.TrackLengthMeters = BinaryPrimitives.ReadUInt16LittleEndian(buffer[offset..]);
        offset += 2;

        state.SessionType = buffer[offset++];
        _rawTrackId = unchecked((sbyte)buffer[offset++]);
        state.RawTrackId = _rawTrackId;
        var trackId = F1UdpConstants.ResolveTrackId(_rawTrackId, state.TrackLengthMeters);

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
        var carIndex = state.PlayerCarIndex;
        if (carIndex >= Profile.MaxCars)
            return;

        state.ResolvedCarIndex = carIndex;
        if (CaptureVerboseDebug)
            UpdateCarRows(buffer, state);

        var offset = Profile.HeaderSize + carIndex * Profile.LapDataSize;
        if (buffer.Length < offset + F1UdpConstants.LapDataDriverStatusOffset + 1)
            return;

        _rawLastLapMs = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
        _rawCurrentLapMs = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 4)..]);
        state.DriverStatus = buffer[offset + F1UdpConstants.LapDataDriverStatusOffset];
        state.CurrentLapInvalid = buffer[offset + F1UdpConstants.LapDataCurrentLapInvalidOffset];

        if (_rawCurrentLapMs > 0)
            state.CurrentLapTimeMs = _rawCurrentLapMs;

        if (_seedLastLapWithoutRecording)
        {
            _seedLastLapWithoutRecording = false;

            // If the game still broadcasts the previous last lap right after a restart,
            // adopt it without recording. If lastLap is already 0, this is a fresh session
            // and the next real completion must be recorded.
            if (_rawLastLapMs > 0)
            {
                state.CurrentLastLapMs = _rawLastLapMs;
                if (!state.SessionBestLapMs.HasValue || _rawLastLapMs < state.SessionBestLapMs)
                    state.SessionBestLapMs = _rawLastLapMs;
                _previousCurrentLapInvalid = state.CurrentLapInvalid;
                return;
            }
        }

        if (_rawLastLapMs == 0)
        {
            _previousCurrentLapInvalid = state.CurrentLapInvalid;
            return;
        }

        var isNewLap = state.CurrentLastLapMs != _rawLastLapMs;
        state.CurrentLastLapMs = _rawLastLapMs;

        if (!state.SessionBestLapMs.HasValue || _rawLastLapMs < state.SessionBestLapMs)
            state.SessionBestLapMs = _rawLastLapMs;

        if (isNewLap && _previousCurrentLapInvalid == 0)
        {
            lapCompleted = true;
            completedLapMs = _rawLastLapMs;
        }

        _previousCurrentLapInvalid = state.CurrentLapInvalid;
    }

    private void BeginNewSession(TelemetryState state)
    {
        state.ResetLapData();
        _previousCurrentLapInvalid = 0;
        _seedLastLapWithoutRecording = true;
    }

    private void UpdateCarRows(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        var rows = new CarLapDebugRow[Profile.MaxCars];
        for (byte i = 0; i < Profile.MaxCars; i++)
        {
            var offset = Profile.HeaderSize + i * Profile.LapDataSize;
            if (buffer.Length < offset + F1UdpConstants.LapDataDriverStatusOffset + 1)
                continue;

            rows[i] = new CarLapDebugRow
            {
                CarIndex = i,
                LastLapMs = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]),
                CurrentLapMs = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 4)..]),
                DriverStatus = buffer[offset + F1UdpConstants.LapDataDriverStatusOffset],
                IsPlayerCar = i == state.PlayerCarIndex,
                IsResolvedCar = i == state.ResolvedCarIndex,
                CurrentLapInvalid = buffer[offset + F1UdpConstants.LapDataCurrentLapInvalidOffset],
            };
        }

        _carRows = rows;
    }

    private void ParseEventPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref bool sessionStarted,
        ref bool sessionEnded)
    {
        if (buffer.Length < Profile.HeaderSize + 4)
            return;

        var code = Encoding.ASCII.GetString(buffer[Profile.HeaderSize..(Profile.HeaderSize + 4)]);
        state.LastEventCode = code;

        sessionStarted = code == "SSTA";
        sessionEnded = code == "SEND";

        if (sessionStarted)
            BeginNewSession(state);
    }

    private void ParseTimeTrialPacket(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        var offset = Profile.HeaderSize;

        if (buffer.Length < offset + Profile.TimeTrialDataSetSize)
            return;

        _timeTrialSessionBestMs = ReadTimeTrialLapTime(buffer, offset);
        if (_timeTrialSessionBestMs > 0)
            state.SessionBestLapMs = _timeTrialSessionBestMs;

        offset += Profile.TimeTrialDataSetSize;
        if (buffer.Length < offset + Profile.TimeTrialDataSetSize)
            return;

        _timeTrialPersonalBestMs = ReadTimeTrialLapTime(buffer, offset);
        if (_timeTrialPersonalBestMs > 0)
            state.PersonalBestLapMs = _timeTrialPersonalBestMs;
    }

    private void ParseSessionHistoryPacket(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        if (buffer.Length < F1UdpConstants.SessionHistoryLapDataOffset + 4)
            return;

        var carIdx = buffer[Profile.HeaderSize];
        if (carIdx >= Profile.MaxCars || carIdx != state.PlayerCarIndex)
            return;

        var numLaps = buffer[Profile.HeaderSize + 1];
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
    }

    private uint ReadTimeTrialLapTime(ReadOnlySpan<byte> buffer, int dataSetOffset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[(dataSetOffset + Profile.TimeTrialLapTimeOffset)..]);
}
