using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.App.ViewModels;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;

namespace MT_F1Chronos.App.Windows;

public partial class ScoresWindow : Window
{
    private const string AllPlayersLabel = "Tous les joueurs";

    private readonly BoardSourceViewModel _source;
    private readonly int? _preferredTrackId;

    private IScoreBoardQuery _board;
    private IReadOnlyList<TrackSummary> _tracks = [];
    private int _currentIndex;
    private bool _ready;
    private bool _bestPerPlayer;
    private string? _playerFilter;

    public ScoresWindow(
        SessionStore globalStore,
        ContestStore contests,
        int? initialTrackId = null,
        string? initialContestId = null,
        bool bestPerPlayer = false,
        AppController? controller = null)
    {
        _ = controller;
        _source = new BoardSourceViewModel(globalStore, contests, initialContestId);
        _preferredTrackId = initialTrackId;
        _bestPerPlayer = bestPerPlayer;
        _board = _source.Query;

        InitializeComponent();
        SyncBestPerPlayerButton();
        PopulateSources(initialContestId);
        _ready = true;
        ApplySelectedSource(keepTrackId: initialTrackId);
    }

    private void PopulateSources(string? initialContestId)
    {
        SourceCombo.Items.Clear();
        var selectedIndex = 0;
        var index = 0;
        foreach (var option in _source.ListSources())
        {
            SourceCombo.Items.Add(option);
            if (!string.IsNullOrWhiteSpace(initialContestId) &&
                string.Equals(option.ContestId, initialContestId, StringComparison.Ordinal))
                selectedIndex = index;
            index++;
        }

        SourceCombo.DisplayMemberPath = nameof(BoardSourceOption.Label);
        SourceCombo.SelectedIndex = selectedIndex;
    }

    private void ApplySelectedSource(int? keepTrackId)
    {
        var option = SourceCombo.SelectedItem as BoardSourceOption;
        _source.Select(option?.ContestId);
        _board = _source.Query;

        if (option?.ContestId is { Length: > 0 })
        {
            WindowTitleText.Text = option.Label;
            Title = option.Label;
        }
        else
        {
            WindowTitleText.Text = "Scores par circuit";
            Title = "Scores par circuit";
        }

        _tracks = _board.GetTracksWithScores();
        _currentIndex = ResolveInitialIndex(keepTrackId ?? _preferredTrackId);
        RefreshPlayerFilter(keepSelection: false);
        RefreshView();
    }

    private int ResolveInitialIndex(int? initialTrackId)
    {
        if (_tracks.Count == 0)
            return 0;

        if (initialTrackId.HasValue)
        {
            for (var i = 0; i < _tracks.Count; i++)
            {
                if (_tracks[i].TrackId == initialTrackId.Value)
                    return i;
            }
        }

        return 0;
    }

    private void RefreshPlayerFilter(bool keepSelection)
    {
        var previous = keepSelection ? _playerFilter : null;
        PlayerCombo.SelectionChanged -= OnPlayerFilterChanged;
        PlayerCombo.Items.Clear();
        PlayerCombo.Items.Add(AllPlayersLabel);

        if (_tracks.Count > 0)
        {
            foreach (var name in _board.GetPlayerNamesForTrack(_tracks[_currentIndex].TrackId))
                PlayerCombo.Items.Add(name);
        }

        var select = AllPlayersLabel;
        if (!string.IsNullOrWhiteSpace(previous) &&
            PlayerCombo.Items.Cast<object>().Any(i =>
                string.Equals(i?.ToString(), previous, StringComparison.OrdinalIgnoreCase)))
            select = previous!;

        PlayerCombo.SelectedItem = select;
        _playerFilter = select == AllPlayersLabel ? null : select;
        PlayerCombo.SelectionChanged += OnPlayerFilterChanged;
    }

    private void RefreshView()
    {
        ScoresPanel.Children.Clear();
        SyncBestPerPlayerButton();

        if (_tracks.Count == 0)
        {
            TrackTitleText.Text = "Aucun circuit";
            TrackIndexText.Text = string.Empty;
            PrevTrackButton.IsEnabled = false;
            NextTrackButton.IsEnabled = false;
            ScoresPanel.Children.Add(CreateMessage("Aucun score enregistré.\nLance un tour valide pour peupler ce classement."));
            return;
        }

        var track = _tracks[_currentIndex];
        TrackTitleText.Text = track.TrackName;
        var modeHint = _bestPerPlayer ? " · meilleur / joueur" : string.Empty;
        var playerHint = string.IsNullOrWhiteSpace(_playerFilter) ? string.Empty : $" · {_playerFilter}";
        TrackIndexText.Text = $"Circuit {_currentIndex + 1} / {_tracks.Count} · {track.ScoreCount} chrono(s){modeHint}{playerHint}";
        PrevTrackButton.IsEnabled = _tracks.Count > 1;
        NextTrackButton.IsEnabled = _tracks.Count > 1;

        var scores = _board.GetScoresForTrack(track.TrackId, _bestPerPlayer, _playerFilter);
        if (scores.Count == 0)
        {
            ScoresPanel.Children.Add(CreateMessage("Aucun score pour ce filtre.\nChange de joueur ou désactive « Meilleur / joueur »."));
            return;
        }

        ScoresPanel.Children.Add(LeaderboardRowUi.CreateHeader());
        foreach (var score in scores)
            ScoresPanel.Children.Add(LeaderboardRowUi.CreateRow(score));
    }

    private void SyncBestPerPlayerButton()
    {
        BestPerPlayerButton.Content = _bestPerPlayer ? "Meilleur" : "Tous";
        BestPerPlayerButton.Background = UiBrushes.FromHex(_bestPerPlayer ? "#FFE10600" : "#FF161B22");
        BestPerPlayerButton.BorderBrush = UiBrushes.FromHex(_bestPerPlayer ? "#FFE10600" : "#33FFFFFF");
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;

        var keepTrackId = _tracks.Count > 0 ? _tracks[_currentIndex].TrackId : _preferredTrackId;
        ApplySelectedSource(keepTrackId);
    }

    private void OnPlayerFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;

        var selected = PlayerCombo.SelectedItem?.ToString();
        _playerFilter = string.IsNullOrWhiteSpace(selected) || selected == AllPlayersLabel
            ? null
            : selected;
        RefreshView();
    }

    private void OnBestPerPlayerClick(object sender, RoutedEventArgs e)
    {
        _bestPerPlayer = !_bestPerPlayer;
        RefreshView();
    }

    private void OnPrevTrackClick(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count <= 1)
            return;

        _currentIndex = (_currentIndex - 1 + _tracks.Count) % _tracks.Count;
        RefreshPlayerFilter(keepSelection: true);
        RefreshView();
    }

    private void OnNextTrackClick(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count <= 1)
            return;

        _currentIndex = (_currentIndex + 1) % _tracks.Count;
        RefreshPlayerFilter(keepSelection: true);
        RefreshView();
    }

    private static UIElement CreateMessage(string text) =>
        new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Foreground = UiBrushes.FromHex("#88FFFFFF"),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        };

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        DragMove();
    }
}
