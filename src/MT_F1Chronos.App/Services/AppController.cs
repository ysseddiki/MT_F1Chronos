using System.Windows;
using System.Windows.Threading;
using MT_F1Chronos.App.Windows;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.App.Services;

public sealed class AppController : IDisposable
{
    private readonly UdpTelemetryListener _listener = new();
    private readonly SessionStore _store = new();
    private readonly ContestStore _contests = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly ScoreExportService _export = new();
    private readonly OverlayCoordinator _overlayUi;
    private readonly Dispatcher _dispatcher;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _refreshTimer;

    private OverlayWindow? _overlay;
    private DebugWindow? _debugWindow;
    private bool _promptOpen;

    public AppController()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _settings = _settingsStore.Load();
        _store.Load();
        _contests.Load();
        NormalizeOverlayContestSetting();
        _overlayUi = new OverlayCoordinator(
            _store,
            _contests,
            _settings,
            () => _listener.State,
            SaveSettings);
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

        AdminPassword.EnsureConfigured(_overlay);

        // Ask for the player name on every launch (pre-filled with the last used name).
        PromptPlayerName(required: true);

        // Show persisted TOP 5 immediately (before UDP track id is known).
        RefreshOverlay();

        TryStartListener();
        _refreshTimer.Start();
    }

    private void TryStartListener()
    {
        try
        {
            _listener.Start(_settings.UdpPort);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _overlay!,
                $"Impossible d'écouter la télémétrie sur le port UDP {_settings.UdpPort}.\n" +
                "Vérifie qu'aucune autre application (ou une seconde instance de F1 Chronos) " +
                "n'utilise déjà ce port.\n\n" +
                $"Détail : {ex.Message}",
                "F1 Chronos — UDP",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void PositionOverlay()
    {
        if (_overlay is null)
            return;

        _overlayUi.Position(_overlay);
    }

    public void SetOverlayWidth(double width)
    {
        _settings.OverlayWidth = Math.Clamp(width, OverlaySizes.Default, OverlaySizes.Max);
        SaveSettings();

        if (_overlay is not null)
            _overlay.Width = _settings.OverlayWidth;
    }

    public void SaveOverlayPosition(double left, double top, double width)
    {
        var workArea = SystemParameters.WorkArea;
        _settings.OverlayTop = Math.Max(0, top - workArea.Top);
        _settings.OverlayRight = Math.Max(0, workArea.Right - left - width);
        _settings.OverlayWidth = Math.Clamp(width, OverlaySizes.Default, OverlaySizes.Max);
        SaveSettings();
    }

    public void SetLeaderboardSize(int size)
    {
        _settings.LeaderboardSize = LeaderboardSizes.Normalize(size);
        SaveSettings();
        RefreshOverlay();
    }

    public void SetBestPerPlayer(bool enabled)
    {
        _settings.BestPerPlayer = enabled;
        SaveSettings();
        RefreshOverlay();
    }

    public bool GetBestPerPlayer() => _settings.BestPerPlayer;

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
            var name = prompt.PlayerName.Trim();
            if (name.Length > OverlaySizes.MaxPlayerNameLength)
                name = name[..OverlaySizes.MaxPlayerNameLength];
            _settings.PlayerName = name;
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
        var window = new ScoresWindow(
            _store,
            _contests,
            currentTrackId >= 0 ? currentTrackId : null,
            initialContestId: null,
            bestPerPlayer: _settings.BestPerPlayer,
            controller: this)
        {
            Owner = _overlay,
        };
        window.ShowDialog();
    }

    public void ShowContestScores(string contestId)
    {
        if (_overlay is null)
            return;

        if (_contests.Get(contestId) is null)
        {
            MessageBox.Show(_overlay, "Concours introuvable.", "Concours", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var currentTrackId = _listener.State.TrackId;
        var window = new ScoresWindow(
            _store,
            _contests,
            currentTrackId >= 0 ? currentTrackId : null,
            initialContestId: contestId,
            bestPerPlayer: _settings.BestPerPlayer,
            controller: this)
        {
            Owner = _overlay,
        };
        window.ShowDialog();
    }

    public void NotifyScoresChanged() => RefreshOverlay();

    /// <summary>Dedicated score management UI — only reachable from admin (password-gated).</summary>
    public void ShowManageScores()
    {
        if (_overlay is null)
            return;

        var currentTrackId = _listener.State.TrackId;
        var window = new ManageScoresWindow(
            _store,
            _contests,
            this,
            currentTrackId >= 0 ? currentTrackId : null)
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

    public OverlayDisplayMode GetOverlayDisplayMode()
    {
        if (!_settings.ShowContestOnOverlay)
            return OverlayDisplayMode.GlobalOnly;

        return _settings.HideGlobalWhenContest
            ? OverlayDisplayMode.ContestOnly
            : OverlayDisplayMode.GlobalAndContest;
    }

    public void SetOverlayDisplayMode(OverlayDisplayMode mode)
    {
        switch (mode)
        {
            case OverlayDisplayMode.ContestOnly:
                _settings.ShowContestOnOverlay = true;
                _settings.HideGlobalWhenContest = true;
                break;
            case OverlayDisplayMode.GlobalOnly:
                _settings.ShowContestOnOverlay = false;
                _settings.HideGlobalWhenContest = false;
                break;
            default:
                _settings.ShowContestOnOverlay = true;
                _settings.HideGlobalWhenContest = false;
                break;
        }

        SaveSettings();
        RefreshOverlay();
    }

    public void SetContestLeaderboardSize(int size)
    {
        _settings.ContestLeaderboardSize = LeaderboardSizes.Normalize(size);
        SaveSettings();
        RefreshOverlay();
    }

    public void ExportScores(string format, string? contestId = null, int? trackId = null)
    {
        IReadOnlyList<ChronoEntry> entries;
        string filePrefix;

        if (!string.IsNullOrWhiteSpace(contestId))
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

            entries = _contests.GetAllScoredEntries(contestId);
            filePrefix = $"contest-{safeName}";
        }
        else
        {
            entries = _store.GetAllScoredEntries();
            filePrefix = "scores";
        }

        if (trackId is >= 0)
        {
            entries = entries.Where(e => e.TrackId == trackId.Value).ToList();
            var trackName = entries.FirstOrDefault()?.TrackName
                            ?? _store.GetTracksWithScores().FirstOrDefault(t => t.TrackId == trackId.Value)?.TrackName
                            ?? $"track-{trackId.Value}";
            var safeTrack = string.Concat(trackName.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
            if (string.IsNullOrWhiteSpace(safeTrack))
                safeTrack = $"track-{trackId.Value}";
            filePrefix += $"-{safeTrack}";
        }

        ExportEntries(entries, format, filePrefix);
    }

    public IReadOnlyList<TrackSummary> ListExportTracks(string? contestId)
    {
        if (!string.IsNullOrWhiteSpace(contestId))
            return _contests.GetTracksWithScores(contestId);

        return _store.GetTracksWithScores();
    }

    private void ExportEntries(IReadOnlyList<ChronoEntry> entries, string format, string filePrefix)
    {
        if (_overlay is null)
            return;

        _export.Export(_overlay, entries, format, filePrefix);
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

        AdminPassword.EnsureConfigured(_overlay);

        var prompt = new PasswordPromptWindow(title, message) { Owner = _overlay };
        if (prompt.ShowDialog() != true)
            return false;

        if (!AdminPassword.Verify(prompt.Password))
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

                RefreshOverlay();
                _overlay?.FlashLapRecorded(_settings.PlayerName);
                return;
            }

            // Live chrono follows UDP packets; leaderboard / headers stay on events + 250 ms timer.
            _overlay?.UpdateLiveChrono(update.State.CurrentLapTimeMs);

            if (update.LapCompleted ||
                update.TrackChanged ||
                update.SessionStarted ||
                update.SessionEnded)
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

        _overlayUi.Refresh(_overlay);
    }

    private void NormalizeOverlayContestSetting()
    {
        if (string.IsNullOrWhiteSpace(_settings.OverlayContestId))
            return;

        if (_contests.Get(_settings.OverlayContestId) is null)
            _settings.OverlayContestId = string.Empty;
    }

    private void SaveSettings() => _settingsStore.Save(_settings);

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
