using System.Buffers.Binary;
using System.Text;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.Tests.Telemetry;

/// <summary>Minimal F1 UDP packet builder for format 2025 fixtures (no game capture needed).</summary>
internal static class UdpPacketBuilder
{
    public static UdpFormatProfile Profile => UdpFormatProfile.Format2025;

    public static byte[] Header(byte packetId, ulong sessionUid = 1, byte playerCarIndex = 0)
    {
        var buffer = new byte[Profile.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), Profile.Format);
        buffer[Profile.PacketIdOffset] = packetId;
        BinaryPrimitives.WriteUInt64LittleEndian(
            buffer.AsSpan(Profile.SessionUidOffset, 8), sessionUid);
        buffer[Profile.PlayerCarIndexOffset] = playerCarIndex;
        return buffer;
    }

    public static byte[] SessionPacket(
        sbyte trackId,
        ushort trackLengthMeters = 5891,
        ulong sessionUid = 1,
        byte sessionType = 1,
        byte gameMode = 0)
    {
        // Enough room past marshal/weather blocks + gameMode byte.
        var buffer = new byte[Profile.HeaderSize + 900];
        Header(F1UdpConstants.PacketSession, sessionUid).CopyTo(buffer, 0);

        var offset = Profile.HeaderSize;
        offset += 4; // weather, track temp, air temp, total laps
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), trackLengthMeters);
        offset += 2;
        buffer[offset++] = sessionType;
        buffer[offset++] = unchecked((byte)trackId);

        // Skip to gameMode (same layout as parser).
        offset += 1 + 2 + 2 + 1 + 1 + 1 + 1 + 1 + 1;
        offset += F1UdpConstants.MarshalZoneCount * F1UdpConstants.MarshalZoneSize;
        offset += 1 + 1 + 1;
        offset += F1UdpConstants.WeatherForecastSampleCount * Profile.WeatherForecastSampleSize;
        offset += 1 + 1 + 12 + 3 + 9;
        buffer[offset] = gameMode;
        return buffer;
    }

    public static byte[] LapDataPacket(
        uint lastLapMs,
        uint currentLapMs,
        byte currentLapInvalid = 0,
        byte driverStatus = 1,
        ulong sessionUid = 1,
        byte playerCarIndex = 0)
    {
        var size = Profile.HeaderSize + Profile.MaxCars * Profile.LapDataSize;
        var buffer = new byte[size];
        Header(F1UdpConstants.PacketLapData, sessionUid, playerCarIndex).CopyTo(buffer, 0);

        var offset = Profile.HeaderSize + playerCarIndex * Profile.LapDataSize;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), lastLapMs);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 4, 4), currentLapMs);
        buffer[offset + F1UdpConstants.LapDataCurrentLapInvalidOffset] = currentLapInvalid;
        buffer[offset + F1UdpConstants.LapDataDriverStatusOffset] = driverStatus;
        return buffer;
    }

    public static byte[] EventPacket(string code, ulong sessionUid = 1)
    {
        if (code.Length != 4)
            throw new ArgumentException("Event code must be 4 ASCII chars.", nameof(code));

        var buffer = new byte[Profile.HeaderSize + 4];
        Header(F1UdpConstants.PacketEvent, sessionUid).CopyTo(buffer, 0);
        Encoding.ASCII.GetBytes(code).CopyTo(buffer, Profile.HeaderSize);
        return buffer;
    }
}
