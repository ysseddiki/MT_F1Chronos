using System.Windows.Media;

namespace MT_F1Chronos.App;

/// <summary>
/// Caches frozen <see cref="SolidColorBrush"/> instances parsed from hex color strings.
/// Shared by the overlay/scores/debug windows so rows rebuilt on every refresh don't
/// keep re-parsing and re-allocating brushes for the same handful of colors.
/// </summary>
internal static class UiBrushes
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public static SolidColorBrush FromHex(string hex)
    {
        if (Cache.TryGetValue(hex, out var cached))
            return cached;

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        Cache[hex] = brush;
        return brush;
    }
}
