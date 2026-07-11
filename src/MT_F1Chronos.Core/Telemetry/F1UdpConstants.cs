namespace MT_F1Chronos.Core.Telemetry;

public static class F1UdpConstants
{
    public const ushort PacketFormat = 2026;
    public const int DefaultPort = 20777;
    public const int HeaderSize = 29;
    public const int MaxCars = 24;
    public const int LapDataSize = 57;

    public const byte PacketSession = 1;
    public const byte PacketLapData = 2;
    public const byte PacketEvent = 3;
    public const byte PacketTimeTrial = 14;
    public const byte PacketSessionHistory = 11;

    public const int LapDataDriverStatusOffset = 44;
    public const int LapHistoryDataSize = 14;
    public const int SessionHistoryLapDataOffset = 36;

    public const byte SessionTypeTimeTrial = 18;
    public const byte GameModeTimeTrial = 5;

    public const int MarshalZoneSize = 5;
    public const int MarshalZoneCount = 21;
    public const int WeatherForecastSampleSize = 9;
    public const int WeatherForecastSampleCount = 64;
    public const int TimeTrialDataSetSize = 25;

    public static readonly IReadOnlyDictionary<int, string> TrackNames = new Dictionary<int, string>
    {
        [0] = "Melbourne",
        [2] = "Shanghai",
        [3] = "Sakhir",
        [4] = "Catalunya",
        [5] = "Monaco",
        [6] = "Montreal",
        [7] = "Silverstone",
        [9] = "Hungaroring",
        [10] = "Spa",
        [11] = "Monza",
        [12] = "Singapore",
        [13] = "Suzuka",
        [14] = "Abu Dhabi",
        [15] = "Texas",
        [16] = "Brasil",
        [17] = "Autriche",
        [19] = "Mexico",
        [20] = "Baku",
        [26] = "Zandvoort",
        [27] = "Imola",
        [29] = "Jeddah",
        [30] = "Miami",
        [31] = "Las Vegas",
        [32] = "Losail",
        [39] = "Silverstone (R)",
        [40] = "Autriche (R)",
        [41] = "Zandvoort (R)",
        [42] = "Madrid",
    };

    public static string GetTrackName(int trackId) =>
        TrackNames.TryGetValue(trackId, out var name) ? name : $"Circuit #{trackId}";
}
