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
    private readonly ContestStore _contests = new();
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
        _contests.Load();
        NormalizeOverlayContestSetting();
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

        // Ask for the player name on every launch (pre-filled with the last used name).
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

    public void SetLeaderboardSize(int size)
    {
        _settings.LeaderboardSize = LeaderboardSizes.Normalize(size);
        SaveSettings();
        RefreshOverlay();
    }

    public void PromptPlayerName(bool required = false)
    {
        if (_promptOpen)
            return;

        _promptOpen = true;

        var recentNames = _store.GetRecentPlayerNames();
        var prompt = new PlayerNameWindow(_settings.PlayerName, recentNames);
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

    public void ShowAdminWindow()
    {
        if (_overlay is null)
            return;

        if (!ConfirmAdminPassword(
                "Administration",
                "Mot de passe requis pour ouvrir l’administration."))
            return;

        var window = new AdminWindow(this, _settings) { Owner = _overlay };
        window.ShowDialog();
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

    public void ShowContestScores(string contestId)
    {
        if (_overlay is null)
            return;

        var contest = _contests.Get(contestId);
        if (contest is null)
        {
            MessageBox.Show(_overlay, "Concours introuvable.", "Concours", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var currentTrackId = _listener.State.TrackId;
        var window = new ScoresWindow(
            _contests.AsScoreBoard(contestId),
            currentTrackId >= 0 ? currentTrackId : null,
            title: $"Concours — {contest.Name}")
        {
            Owner = _overlay,
        };
        window.ShowDialog();
    }

    public IReadOnlyList<Contest> ListContests() => _contests.List();

    public int GetContestEntryCount(string contestId) => _contests.EntryCount(contestId);

    public Contest? CreateContest(string name)
    {
        if (_overlay is null)
            return null;

        try
        {
            var contest = _contests.Create(name, startImmediately: true);
            RefreshOverlay();
            return contest;
        }
        catch (Exception ex)
        {
            MessageBox.Show(_overlay, ex.Message, "Concours", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    public bool StartContest(string contestId)
    {
        var ok = _contests.Start(contestId);
        RefreshOverlay();
        return ok;
    }

    public bool StopContest(string contestId)
    {
        var ok = _contests.Stop(contestId);
        RefreshOverlay();
        return ok;
    }

    public bool DeleteContest(string contestId)
    {
        if (_overlay is null)
            return false;

        var contest = _contests.Get(contestId);
        if (contest is null)
            return false;

        var confirm = MessageBox.Show(
            _overlay,
            $"Supprimer le concours « {contest.Name} » et tous ses scores ?\nCette action est irréversible.",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return false;

        var ok = _contests.Delete(contestId);
        if (ok && string.Equals(_settings.OverlayContestId, contestId, StringComparison.Ordinal))
            SetOverlayContest(null);

        RefreshOverlay();
        return ok;
    }

    public void SetOverlayContest(string? contestId)
    {
        if (string.IsNullOrWhiteSpace(contestId))
        {
            _settings.OverlayContestId = string.Empty;
        }
        else if (_contests.Get(contestId) is null)
        {
            _settings.OverlayContestId = string.Empty;
        }
        else
        {
            _settings.OverlayContestId = contestId;
        }

        SaveSettings();
        RefreshOverlay();
    }

    public void SetShowContestOnOverlay(bool show)
    {
        _settings.ShowContestOnOverlay = show;
        SaveSettings();
        RefreshOverlay();
    }

    public void ExportScores(string format) => ExportEntries(_store.GetAllScoredEntries(), format, "scores");

    public void ExportContestScores(string contestId, string format)
    {
        var contest = _contests.Get(contestId);
        if (contest is null)
        {
            if (_overlay is not null)
                MessageBox.Show(_overlay, "Concours introuvable.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var safeName = string.Concat(contest.Name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "contest";

        ExportEntries(_contests.GetAllScoredEntries(contestId), format, $"contest-{safeName}");
    }

    private void ExportEntries(IReadOnlyList<ChronoEntry> entries, string format, string filePrefix)
    {
        if (_overlay is null)
            return;

        if (entries.Count == 0)
        {
            MessageBox.Show(_overlay, "Aucun score à exporter.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dialog = new SaveFileDialog
        {
            Title = "Exporter les scores",
            FileName = $"MT_F1Chronos-{filePrefix}-{stamp}",
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

    private bool ConfirmAdminPassword(string title, string message)
    {
        if (_overlay is null)
            return false;

        var prompt = new PasswordPromptWindow(title, message) { Owner = _overlay };
        if (prompt.ShowDialog() != true)
            return false;

        if (!string.Equals(prompt.Password, ScoreResetPassword, StringComparison.Ordinal))
        {
            MessageBox.Show(_overlay, "Mot de passe incorrect.", "Administration", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                _contests.RecordCompletedLap(
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

        var size = _settings.LeaderboardSize;
        var showContest = false;
        var contestLabel = string.Empty;
        IReadOnlyList<LeaderboardRow> contestBoard = [];

        if (_settings.ShowContestOnOverlay && !string.IsNullOrWhiteSpace(_settings.OverlayContestId))
        {
            var contest = _contests.Get(_settings.OverlayContestId);
            if (contest is not null)
            {
                showContest = true;
                contestLabel = contest.Name;
                var trackId = _store.ResolveOverlayTrackId(_listener.State);
                if (trackId < 0)
                    trackId = _contests.GetTracksWithScores(contest.Id).FirstOrDefault()?.TrackId ?? -1;

                contestBoard = trackId >= 0
                    ? _contests.GetLeaderboard(contest.Id, trackId, LeaderboardSizes.Extended)
                    : [];
            }
            else
            {
                _settings.OverlayContestId = string.Empty;
                SaveSettings();
            }
        }

        _overlay.UpdateSnapshot(_store.BuildSnapshot(
            _listener.State,
            _settings.PlayerName,
            size,
            showContestLeaderboard: showContest,
            contestLabel: contestLabel,
            contestLeaderboard: contestBoard));
    }

    private void NormalizeOverlayContestSetting()
    {
        if (string.IsNullOrWhiteSpace(_settings.OverlayContestId))
            return;

        if (_contests.Get(_settings.OverlayContestId) is null)
            _settings.OverlayContestId = string.Empty;
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
            settings.UdpFormat = settings.UdpFormat is 2026 ? 2026 : 2025;
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
        _store.Dispose();
        _contests.Dispose();
        _listener.Dispose();
        _debugWindow?.Close();
        _overlay?.Close();
    }
}
