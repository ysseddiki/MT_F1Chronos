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

    /// <summary>Fallback track ID from length when raw ID is unknown (&lt; 0).</summary>
    public static int ResolveTrackId(int rawTrackId, ushort trackLengthMeters)
    {
        if (rawTrackId >= 0)
            return rawTrackId;

        if (trackLengthMeters > 0 &&
            TrackLengthToId.TryGetValue(trackLengthMeters, out var resolved))
            return resolved;

        return rawTrackId;
    }

    private static readonly Dictionary<ushort, int> TrackLengthToId = new()
    {
        [5273] = 0,   // Melbourne
        [5451] = 2,   // Shanghai
        [5412] = 3,   // Sakhir
        [4675] = 4,   // Catalunya
        [3337] = 5,   // Monaco
        [4361] = 6,   // Montreal
        [5891] = 7,   // Silverstone
        [4381] = 9,   // Hungaroring
        [7004] = 10,  // Spa
        [5793] = 11,  // Monza
        [5063] = 12,  // Singapore
        [5807] = 13,  // Suzuka
        [5281] = 14,  // Abu Dhabi
        [5513] = 15,  // Texas
        [4309] = 16,  // Brasil
        [4318] = 17,  // Austria
        [4304] = 19,  // Mexico
        [6003] = 20,  // Baku
        [4252] = 26,  // Zandvoort
        [4954] = 27,  // Imola
        [6174] = 29,  // Jeddah
        [5417] = 30,  // Miami
        [6201] = 31,  // Las Vegas
        [5410] = 32,  // Losail
        [5474] = 42,  // Madrid
    };
}
