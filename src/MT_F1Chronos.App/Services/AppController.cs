using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MT_F1Chronos.App.Windows;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.App.Services;

public sealed class AppController : IDisposable
{
    private const string ScoreResetPassword = "ys-reset-mt26";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly UdpTelemetryListener _listener = new();
    private readonly SessionStore _store = new();
    private readonly Dispatcher _dispatcher;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _refreshTimer;

    private OverlayWindow? _overlay;
    private DebugWindow? _debugWindow;
    private bool _promptOpen;

    public AppController()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _settings = LoadSettings();
        _store.Load();
        _listener.SetFormat((ushort)_settings.UdpFormat);
        _listener.UpdateReceived += OnTelemetryUpdate;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += (_, _) => RefreshOverlay();
    }

    public OverlayWindow CreateOverlay()
    {
        _overlay = new OverlayWindow(_settings, this);
        return _overlay;
    }

    public void Start()
    {
        if (_overlay is null)
            return;

        _overlay.Show();
        PositionOverlay();

        if (string.IsNullOrWhiteSpace(_settings.PlayerName))
            PromptPlayerName(required: true);

        // Show persisted TOP 5 immediately (before UDP track id is known).
        RefreshOverlay();

        _listener.Start(_settings.UdpPort);
        _refreshTimer.Start();
    }

    public void PositionOverlay()
    {
        if (_overlay is null)
            return;

        var workArea = SystemParameters.WorkArea;
        _overlay.Width = _settings.OverlayWidth;
        _overlay.Left = workArea.Right - _settings.OverlayRight - _settings.OverlayWidth;
        _overlay.Top = workArea.Top + _settings.OverlayTop;
    }

    public void SetOverlayWidth(double width)
    {
        _settings.OverlayWidth = width;
        SaveSettings();

        if (_overlay is not null)
            _overlay.Width = width;
    }

    public void SaveOverlayPosition(double left, double top, double width)
    {
        var workArea = SystemParameters.WorkArea;
        _settings.OverlayTop = Math.Max(0, top - workArea.Top);
        _settings.OverlayRight = Math.Max(0, workArea.Right - left - width);
        _settings.OverlayWidth = width;
        SaveSettings();
    }

    public void SetOverlayOpacity(double opacity)
    {
        _settings.OverlayOpacity = Math.Clamp(opacity, AppSettings.MinOpacity, AppSettings.MaxOpacity);
        SaveSettings();
        _overlay?.ApplyOpacity(_settings.OverlayOpacity);
    }

    public void SetLeaderboardSize(int size)
    {
        _settings.LeaderboardSize = LeaderboardSizes.Normalize(size);
        SaveSettings();
        _overlay?.SyncLeaderboardMenu(_settings.LeaderboardSize);
        RefreshOverlay();
    }

    public void PromptPlayerName(bool required = false)
    {
        if (_promptOpen)
            return;

        _promptOpen = true;

        var prompt = new PlayerNameWindow(_settings.PlayerName);
        var accepted = prompt.ShowDialog() == true;

        if (accepted && !string.IsNullOrWhiteSpace(prompt.PlayerName))
        {
            _settings.PlayerName = prompt.PlayerName.Trim();
            SaveSettings();
        }
        else if (required && string.IsNullOrWhiteSpace(_settings.PlayerName))
        {
            _settings.PlayerName = "Joueur";
            SaveSettings();
        }

        _promptOpen = false;
        RefreshOverlay();
    }

    public void ShowAllScores()
    {
        if (_overlay is null)
            return;

        var currentTrackId = _listener.State.TrackId;
        var window = new ScoresWindow(_store, currentTrackId >= 0 ? currentTrackId : null)
        {
            Owner = _overlay,
        };
        window.ShowDialog();
    }

    public void ExportScores(string format)
    {
        if (_overlay is null)
            return;

        var entries = _store.GetAllScoredEntries();
        if (entries.Count == 0)
        {
            MessageBox.Show(_overlay, "Aucun score à exporter.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dialog = new SaveFileDialog
        {
            Title = "Exporter les scores",
            FileName = $"MT_F1Chronos-scores-{stamp}",
            Filter = format switch
            {
                "csv" => "CSV (*.csv)|*.csv",
                "json" => "JSON (*.json)|*.json",
                _ => "HTML (*.html)|*.html",
            },
            DefaultExt = format,
            AddExtension = true,
        };

        if (dialog.ShowDialog(_overlay) != true)
            return;

        try
        {
            switch (format)
            {
                case "csv":
                    ScoreExporter.ExportCsv(entries, dialog.FileName);
                    break;
                case "json":
                    ScoreExporter.ExportJson(entries, dialog.FileName);
                    break;
                default:
                    ScoreExporter.ExportHtml(entries, dialog.FileName);
                    break;
            }

            var open = MessageBox.Show(
                _overlay,
                $"Export terminé :\n{dialog.FileName}\n\nOuvrir le fichier ?",
                "Export",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (open == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dialog.FileName,
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(_overlay, $"Échec de l'export :\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ResetCurrentTrackScores()
    {
        if (_overlay is null)
            return;

        if (!ConfirmResetPassword("Réinitialiser le circuit", "Mot de passe requis pour effacer les scores du circuit affiché."))
            return;

        var trackId = ResolveResetTrackId();
        if (trackId < 0)
        {
            MessageBox.Show(_overlay, "Aucun circuit à réinitialiser.", "Scores", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            _overlay,
            "Effacer tous les scores de ce circuit ? Cette action est irréversible.",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        var removed = _store.ClearScoresForTrack(trackId);
        RefreshOverlay();
        MessageBox.Show(_overlay, $"{removed} score(s) supprimé(s).", "Scores", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ResetAllScores()
    {
        if (_overlay is null)
            return;

        if (!ConfirmResetPassword("Réinitialiser tout", "Mot de passe requis pour effacer TOUS les scores."))
            return;

        var confirm = MessageBox.Show(
            _overlay,
            "Effacer tous les scores de tous les circuits ? Cette action est irréversible.",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        var removed = _store.ClearAllScores();
        RefreshOverlay();
        MessageBox.Show(_overlay, $"{removed} score(s) supprimé(s).", "Scores", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool ConfirmResetPassword(string title, string message)
    {
        if (_overlay is null)
            return false;

        var prompt = new PasswordPromptWindow(title, message) { Owner = _overlay };
        if (prompt.ShowDialog() != true)
            return false;

        if (!string.Equals(prompt.Password, ScoreResetPassword, StringComparison.Ordinal))
        {
            MessageBox.Show(_overlay, "Mot de passe incorrect.", "Scores", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private int ResolveResetTrackId()
    {
        if (_listener.State.TrackId >= 0)
            return _listener.State.TrackId;

        return _store.GetTracksWithScores().FirstOrDefault()?.TrackId ?? -1;
    }

    public void ShowDebugWindow()
    {
        if (_overlay is null)
            return;

        if (_debugWindow is { IsLoaded: true })
        {
            _debugWindow.Activate();
            return;
        }

        _listener.Parser.CaptureVerboseDebug = true;
        _debugWindow = new DebugWindow(this) { Owner = _overlay };
        _debugWindow.Closed += (_, _) =>
        {
            _listener.Parser.CaptureVerboseDebug = false;
            _debugWindow = null;
        };
        _debugWindow.Show();
    }

    public TelemetryDebugSnapshot BuildDebugSnapshot() =>
        _listener.Parser.BuildDebugSnapshot(_listener.State, _store.BuildDebugInfo());

    public void SetUdpFormat(int format)
    {
        _settings.UdpFormat = format is 2026 ? 2026 : 2025;
        _listener.SetFormat((ushort)_settings.UdpFormat);
        _listener.State.ResetLapData();
        SaveSettings();
        RefreshOverlay();
    }

    private void OnTelemetryUpdate(TelemetryUpdate update)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (update.SessionEnded)
                _store.CloseActiveSession();

            if (update.TrackChanged ||
                (update.State.TrackId >= 0 && !_store.HasLiveSession))
                TryEnsureTrackContext(update.State);

            if (update.LapCompleted &&
                update.CompletedLapMs is > 0 &&
                update.State.TrackId >= 0 &&
                !string.IsNullOrWhiteSpace(_settings.PlayerName))
            {
                _store.RecordCompletedLap(
                    _settings.PlayerName,
                    update.State.TrackId,
                    update.State.TrackName,
                    update.CompletedLapMs.Value);
            }

            RefreshOverlay();
        });
    }

    private void TryEnsureTrackContext(TelemetryState state)
    {
        if (state.TrackId < 0)
            return;

        _store.EnsureTrackContext(state.TrackId, state.TrackName);
    }

    private void RefreshOverlay()
    {
        if (_overlay is null)
            return;

        _overlay.UpdateSnapshot(_store.BuildSnapshot(
            _listener.State,
            _settings.PlayerName,
            _settings.LeaderboardSize));
    }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos",
            "settings.json");

    private static AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.OverlayOpacity = Math.Clamp(
                settings.OverlayOpacity <= 0 ? 0.96 : settings.OverlayOpacity,
                AppSettings.MinOpacity,
                AppSettings.MaxOpacity);
            settings.LeaderboardSize = LeaderboardSizes.Normalize(settings.LeaderboardSize);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, JsonOptions));
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _store.CloseActiveSession();
        _store.Save();
        _listener.Dispose();
        _debugWindow?.Close();
        _overlay?.Close();
    }
}
