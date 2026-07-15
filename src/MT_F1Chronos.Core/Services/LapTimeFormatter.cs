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
}
