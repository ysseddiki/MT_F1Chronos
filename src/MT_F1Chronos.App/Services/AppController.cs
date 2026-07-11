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

    private OverlayWindow? _overlay;
    private bool _promptOpen;

    public AppSettings Settings => _settings;
    public SessionStore Store => _store;

    public AppController()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _settings = LoadSettings();
        _store.Load();

        _listener.UpdateReceived += OnTelemetryUpdate;
        _listener.ErrorOccurred += _ => { };
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

        _listener.Start(_settings.UdpPort);

        var refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        refreshTimer.Tick += (_, _) => RefreshOverlay();
        refreshTimer.Start();
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

            if (_store.ActiveSession is not null)
                _store.RenameActiveSession(_settings.PlayerName);
        }
        else if (required && string.IsNullOrWhiteSpace(_settings.PlayerName))
        {
            _settings.PlayerName = "Joueur";
            SaveSettings();
        }

        _promptOpen = false;
        TryEnsureActiveSession(_listener.State);
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

    private void OnTelemetryUpdate(TelemetryUpdate update)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (update.SessionEnded)
                _store.CloseActiveSession();

            if (update.State.TrackId >= 0)
                TryEnsureActiveSession(update.State);

            if (update.CompletedLapMs.HasValue)
                _store.UpdateActiveBest(update.CompletedLapMs.Value);

            if (update.State.EffectiveBestLapMs.HasValue && _store.ActiveSession is not null)
                _store.UpdateActiveBest(update.State.EffectiveBestLapMs.Value);

            RefreshOverlay();
        });
    }

    private void TryEnsureActiveSession(TelemetryState state)
    {
        if (string.IsNullOrWhiteSpace(_settings.PlayerName) || state.TrackId < 0)
            return;

        _store.EnsureActiveSession(
            _settings.PlayerName,
            state.TrackId,
            state.TrackName);
    }

    private void RefreshOverlay()
    {
        if (_overlay is null)
            return;

        var snapshot = _store.BuildSnapshot(_listener.State, _settings.PlayerName);
        _overlay.UpdateSnapshot(snapshot);
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
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    public void Dispose()
    {
        _store.CloseActiveSession();
        _listener.Dispose();
        _overlay?.Close();
    }
}
