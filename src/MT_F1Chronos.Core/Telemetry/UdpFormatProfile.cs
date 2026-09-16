namespace MT_F1Chronos.Core.Telemetry;

public sealed class UdpFormatProfile
{
    public ushort Format { get; init; }
    public int HeaderSize { get; init; }
    public int PacketIdOffset { get; init; }
    public int SessionUidOffset { get; init; }
    public int PlayerCarIndexOffset { get; init; }
    public byte SessionTypeTimeTrial { get; init; }
    public int MaxCars { get; init; }
    public int LapDataSize { get; init; }
    public int WeatherForecastSampleSize { get; init; }
    public int TimeTrialDataSetSize { get; init; }
    public int TimeTrialLapTimeOffset { get; init; }
    public int CarSetupDataSize { get; init; }

    /// <summary>
    /// Offset of <c>m_customSetup</c> within a TimeTrialDataSet
    /// (menu setup perso — pas frein / différentiel MFD).
    /// </summary>
    public int TimeTrialCustomSetupOffset => TimeTrialDataSetSize - 2;

    public static UdpFormatProfile For(ushort format) =>
        format switch
        {
            2025 => Format2025,
            _ => Format2026,
        };

    /// <summary>F1 25 — header 29 octets (m_game_year + m_overall_frame_identifier).</summary>
    public static UdpFormatProfile Format2025 { get; } = new()
    {
        Format = 2025,
        HeaderSize = 29,
        PacketIdOffset = 6,
        SessionUidOffset = 7,
        PlayerCarIndexOffset = 27,
        SessionTypeTimeTrial = 13,
        MaxCars = 22,
        LapDataSize = 57,
        WeatherForecastSampleSize = 9,
        TimeTrialDataSetSize = 24,
        TimeTrialLapTimeOffset = 2,
        CarSetupDataSize = 50,
    };

    /// <summary>F1 26 — même header 29 octets, 24 voitures.</summary>
    public static UdpFormatProfile Format2026 { get; } = new()
    {
        Format = 2026,
        HeaderSize = 29,
        PacketIdOffset = 6,
        SessionUidOffset = 7,
        PlayerCarIndexOffset = 27,
        SessionTypeTimeTrial = 13,
        MaxCars = 24,
        LapDataSize = 57,
        WeatherForecastSampleSize = 9,
        TimeTrialDataSetSize = 25,
        TimeTrialLapTimeOffset = 3,
        CarSetupDataSize = 50,
    };
}
