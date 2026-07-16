namespace MT_F1Chronos.Core.Services;

public static class LapTimeFormatter
{
    public static string Format(uint lapMs)
    {
        if (lapMs == 0)
            return "--:--.---";

        var totalSeconds = lapMs / 1000.0;
        var minutes = (int)(totalSeconds / 60);
        var seconds = totalSeconds - minutes * 60;

        return minutes > 0
            ? $"{minutes}:{seconds:00.000}"
            : $"{seconds:0.000}";
    }

    /// <summary>Formats delta vs P1: negative = ahead, positive = behind.</summary>
    public static string FormatDelta(int deltaMs)
    {
        var sign = deltaMs > 0 ? "+" : deltaMs < 0 ? "-" : "";
        var abs = Math.Abs(deltaMs) / 1000.0;
        return $"{sign}{abs:0.000}";
    }
}
