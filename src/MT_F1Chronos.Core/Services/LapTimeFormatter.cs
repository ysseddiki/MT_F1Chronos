using System.Globalization;

namespace MT_F1Chronos.Core.Services;

public static class LapTimeFormatter
{
    public static string Format(uint lapMs)
    {
        if (lapMs == 0)
            return "--:--.---";

        var minutes = lapMs / 60_000u;
        var seconds = (lapMs % 60_000u) / 1000.0;
        return $"{minutes:00}:{seconds.ToString("00.000", CultureInfo.InvariantCulture)}";
    }
}
