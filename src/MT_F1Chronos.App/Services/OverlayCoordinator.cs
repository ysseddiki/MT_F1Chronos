using System.Windows;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;
using MT_F1Chronos.Core.Telemetry;
using MT_F1Chronos.App.Windows;

namespace MT_F1Chronos.App.Services;

/// <summary>
/// Overlay layout + snapshot refresh — extracted from <see cref="AppController"/>.
/// </summary>
internal sealed class OverlayCoordinator
{
    private readonly SessionStore _store;
    private readonly ContestStore _contests;
    private readonly AppSettings _settings;
    private readonly Func<TelemetryState> _getState;
    private readonly Action _saveSettings;

    public OverlayCoordinator(
        SessionStore store,
        ContestStore contests,
        AppSettings settings,
        Func<TelemetryState> getState,
        Action saveSettings)
    {
        _store = store;
        _contests = contests;
        _settings = settings;
        _getState = getState;
        _saveSettings = saveSettings;
    }

    public void Position(OverlayWindow overlay)
    {
        var workArea = SystemParameters.WorkArea;
        overlay.Width = _settings.OverlayWidth;
        overlay.Left = workArea.Right - _settings.OverlayRight - _settings.OverlayWidth;
        overlay.Top = workArea.Top + _settings.OverlayTop;
    }

    public void Refresh(OverlayWindow overlay)
    {
        var size = LeaderboardSizes.Normalize(_settings.LeaderboardSize);
        var contestSize = LeaderboardSizes.Normalize(_settings.ContestLeaderboardSize);
        var showContest = false;
        var contestLabel = string.Empty;
        IReadOnlyList<LeaderboardRow> contestBoard = [];
        var state = _getState();

        if (_settings.ShowContestOnOverlay && !string.IsNullOrWhiteSpace(_settings.OverlayContestId))
        {
            var contest = _contests.Get(_settings.OverlayContestId);
            if (contest is null)
            {
                _settings.OverlayContestId = string.Empty;
                _saveSettings();
            }
            else if (contest.Status == ContestStatus.Active)
            {
                showContest = true;
                contestLabel = contest.Name;
                var trackId = _store.ResolveOverlayTrackId(state);
                if (trackId < 0)
                    trackId = _contests.GetTracksWithScores(contest.Id).FirstOrDefault()?.TrackId ?? -1;

                contestBoard = trackId >= 0
                    ? _contests.GetLeaderboard(contest.Id, trackId, contestSize, _settings.BestPerPlayer)
                    : [];
            }
        }

        var showGlobal = !(showContest && _settings.HideGlobalWhenContest);

        if (showContest && showGlobal)
            size = LeaderboardSizes.Compact;

        overlay.UpdateSnapshot(_store.BuildSnapshot(
            state,
            _settings.PlayerName,
            size,
            showGlobalLeaderboard: showGlobal,
            showContestLeaderboard: showContest,
            contestLabel: contestLabel,
            contestLeaderboardSize: contestSize,
            contestLeaderboard: contestBoard,
            bestPerPlayer: _settings.BestPerPlayer));
    }
}
