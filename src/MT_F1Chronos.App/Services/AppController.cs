using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using MT_F1Chronos.App.Native;
using MT_F1Chronos.App.Windows;
using MT_F1Chronos.Core.Services;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.App.Services;

public sealed class AppController : IDisposable
{
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

    public void Start()
    {
        _overlay = new OverlayWindow(_settings);
        _overlay.Show();
        PositionOverlay();

        _listener.Start(_settings.UdpPort);

        var refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
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

    public void RequestNamePrompt(bool force = false)
    {
        if (_promptOpen)
            return;

        var state = _listener.State;
        if (!force && !state.IsTimeTrial && state.TrackId < 0)
            return;

        _promptOpen = true;

        if (_overlay is not null)
            ClickThroughHelper.DisableClickThrough(_overlay);

        var defaultName = $"Chrono #{_store.GetNextDefaultNumber()}";
        var recentNames = _store.GetRecentNames().ToList();

        var prompt = new NamePromptWindow(defaultName, recentNames, state.TrackName);
        var accepted = prompt.ShowDialog() == true;

        if (accepted && !string.IsNullOrWhiteSpace(prompt.SessionName))
        {
            _store.StartSession(
                prompt.SessionName.Trim(),
                state.TrackId,
                state.TrackName);
        }

        if (_overlay is not null)
            ClickThroughHelper.EnableClickThrough(_overlay);
        _promptOpen = false;
        RefreshOverlay();
    }

    private void OnTelemetryUpdate(TelemetryUpdate update)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (update.SessionStarted && update.State.IsTimeTrial)
                RequestNamePrompt();

            if (update.SessionEnded)
                _store.CloseActiveSession();

            if (update.CompletedLapMs.HasValue)
                _store.UpdateActiveBest(update.CompletedLapMs.Value);

            if (update.State.EffectiveBestLapMs.HasValue && _store.ActiveSession is not null)
                _store.UpdateActiveBest(update.State.EffectiveBestLapMs.Value);

            RefreshOverlay();
        });
    }

    private void RefreshOverlay()
    {
        if (_overlay is null)
            return;

        var snapshot = _store.BuildSnapshot(_listener.State);
        _overlay.UpdateSnapshot(snapshot);
    }

    private static AppSettings LoadSettings()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos",
            "settings.json");

        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Dispose()
    {
        _store.CloseActiveSession();
        _listener.Dispose();
        _overlay?.Close();
    }
}
