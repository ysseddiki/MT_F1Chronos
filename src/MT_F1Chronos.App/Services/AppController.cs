using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using MT_F1Chronos.App.Windows;
using MT_F1Chronos.Core.Services;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.App.Services;

public sealed class AppController : IDisposable
{
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
            _settings.PlayerName));
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
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
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
