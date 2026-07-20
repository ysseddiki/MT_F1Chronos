using System.Text.Json;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.App.Services;

/// <summary>
/// Loads / saves <see cref="AppSettings"/> under LocalAppData with light migrations.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos",
            "settings.json");
    }

    public string FilePath => _path;

    public AppSettings Load()
    {
        if (!File.Exists(_path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return Migrate(settings);
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Migrate(settings), JsonOptions));
    }

    private static AppSettings Migrate(AppSettings settings)
    {
        settings.UdpFormat = settings.UdpFormat is 2026 ? 2026 : 2025;
        settings.LeaderboardSize = LeaderboardSizes.Normalize(settings.LeaderboardSize);
        settings.ContestLeaderboardSize = settings.ContestLeaderboardSize <= 0
            ? LeaderboardSizes.Extended
            : LeaderboardSizes.Normalize(settings.ContestLeaderboardSize);
        settings.OverlayWidth = Math.Clamp(
            settings.OverlayWidth <= 0 ? OverlaySizes.Default : settings.OverlayWidth,
            OverlaySizes.Default,
            OverlaySizes.Max);
        if (!string.IsNullOrEmpty(settings.PlayerName) &&
            settings.PlayerName.Length > OverlaySizes.MaxPlayerNameLength)
            settings.PlayerName = settings.PlayerName[..OverlaySizes.MaxPlayerNameLength];
        return settings;
    }
}
