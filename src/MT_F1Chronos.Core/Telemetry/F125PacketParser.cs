using System.Buffers.Binary;
using System.Text;

namespace MT_F1Chronos.Core.Telemetry;

public static class F125PacketParser
{
    public static bool TryParse(ReadOnlySpan<byte> buffer, TelemetryState state, out TelemetryUpdate? update)
    {
        update = null;
        if (buffer.Length < F125Constants.HeaderSize)
            return false;

        var packetId = buffer[5];
        var sessionUid = BinaryPrimitives.ReadUInt64LittleEndian(buffer[6..14]);
        state.PlayerCarIndex = BinaryPrimitives.ReadUInt32LittleEndian(buffer[22..26]);
        state.IsReceiving = true;
        state.LastPacketUtc = DateTime.UtcNow;

        var sessionChanged = state.SessionUid != 0 && sessionUid != state.SessionUid;
        state.SessionUid = sessionUid;

        var sessionStarted = false;
        var sessionEnded = false;
        var lapCompleted = false;
        uint? completedLapMs = null;

        switch (packetId)
        {
            case F125Constants.PacketSession:
                ParseSessionPacket(buffer, state);
                break;

            case F125Constants.PacketLapData:
                ParseLapDataPacket(buffer, state, ref lapCompleted, ref completedLapMs);
                break;

            case F125Constants.PacketEvent:
                ParseEventPacket(buffer, state, ref sessionStarted, ref sessionEnded);
                break;

            case F125Constants.PacketTimeTrial:
                ParseTimeTrialPacket(buffer, state);
                break;
        }

        if (sessionChanged)
        {
            sessionStarted = true;
        }

        update = new TelemetryUpdate
        {
            State = state,
            SessionStarted = sessionStarted,
            SessionEnded = sessionEnded,
            LapCompleted = lapCompleted,
            CompletedLapMs = completedLapMs,
        };

        return true;
    }

    private static void ParseSessionPacket(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        if (buffer.Length < 37)
            return;

        state.TrackId = unchecked((sbyte)buffer[36]);
        state.SessionType = buffer[35];
    }

    private static void ParseLapDataPacket(
        ReadOnlySpan<byte> buffer,
        TelemetryState state,
        ref bool lapCompleted,
        ref uint? completedLapMs)
    {
        var playerIndex = (int)state.PlayerCarIndex;
        if (playerIndex < 0 || playerIndex >= F125Constants.MaxCars)
            return;

        var offset = F125Constants.HeaderSize + playerIndex * F125Constants.LapDataSize;
        if (buffer.Length < offset + 8)
            return;

        var lastLap = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
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
        if (buffer.Length < F125Constants.HeaderSize + 4)
            return;

        var code = Encoding.ASCII.GetString(buffer[F125Constants.HeaderSize..(F125Constants.HeaderSize + 4)]);
        state.LastEventCode = code;

        sessionStarted = code == "SSTA";
        sessionEnded = code == "SEND";
    }

    private static void ParseTimeTrialPacket(ReadOnlySpan<byte> buffer, TelemetryState state)
    {
        var offset = F125Constants.HeaderSize + 2;
        if (buffer.Length < offset + 4)
            return;

        var sessionBest = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(F125Constants.HeaderSize + 2)..]);
        if (sessionBest > 0)
            state.SessionBestLapMs = sessionBest;

        var personalOffset = F125Constants.HeaderSize + 26;
        if (buffer.Length >= personalOffset + 4)
        {
            var personalBest = BinaryPrimitives.ReadUInt32LittleEndian(buffer[personalOffset..]);
            if (personalBest > 0)
                state.PersonalBestLapMs = personalBest;
        }
    }
}
