using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;

namespace MT_F1Chronos.App.Windows;

public partial class ScoresWindow : Window
{
    private const string AllPlayersLabel = "Tous les joueurs";

    private readonly SessionStore _globalStore;
    private readonly ContestStore _contests;
    private readonly AppController? _controller;
    private readonly int? _preferredTrackId;

    private IScoreBoardView _board;
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
        _globalStore = globalStore;
        _contests = contests;
        _controller = controller;
        _preferredTrackId = initialTrackId;
        _bestPerPlayer = bestPerPlayer;
        _board = globalStore;

        InitializeComponent();
        SyncBestPerPlayerButton();
        PopulateSources(initialContestId);
        _ready = true;
        ApplySelectedSource(keepTrackId: initialTrackId);
    }

    private void PopulateSources(string? initialContestId)
    {
        SourceCombo.Items.Clear();
        SourceCombo.Items.Add(new SourceOption(null, "Global"));

        var selectedIndex = 0;
        var index = 1;
        foreach (var contest in _contests.List())
        {
            SourceCombo.Items.Add(new SourceOption(contest.Id, $"Concours — {contest.Name}"));
            if (!string.IsNullOrWhiteSpace(initialContestId) &&
                string.Equals(contest.Id, initialContestId, StringComparison.Ordinal))
                selectedIndex = index;
            index++;
        }

        SourceCombo.DisplayMemberPath = nameof(SourceOption.Label);
        SourceCombo.SelectedIndex = selectedIndex;
    }

    private void ApplySelectedSource(int? keepTrackId)
    {
        var option = SourceCombo.SelectedItem as SourceOption;
        if (option?.ContestId is { Length: > 0 } contestId)
        {
            _board = _contests.AsScoreBoard(contestId);
            WindowTitleText.Text = option.Label;
            Title = option.Label;
        }
        else
        {
            _board = _globalStore;
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

        ScoresPanel.Children.Add(CreateHeader());

        foreach (var score in scores)
            ScoresPanel.Children.Add(CreateRow(score));
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

    private void OnDeleteEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LeaderboardRow row } || string.IsNullOrWhiteSpace(row.EntryId))
            return;

        var confirm = MessageBox.Show(
            this,
            $"Supprimer le chrono {row.FormattedTime} de {row.Name} ?",
            "Supprimer un chrono",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        if (!_board.DeleteEntry(row.EntryId))
        {
            MessageBox.Show(this, "Impossible de supprimer ce chrono.", "Scores", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AfterMutation();
    }

    private void OnDeletePlayerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string playerName } || string.IsNullOrWhiteSpace(playerName))
            return;
        if (_tracks.Count == 0)
            return;

        var track = _tracks[_currentIndex];
        var confirm = MessageBox.Show(
            this,
            $"Supprimer tous les chronos de « {playerName} » sur {track.TrackName} ?",
            "Supprimer un joueur",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        var removed = _board.DeletePlayerOnTrack(playerName, track.TrackId);
        if (removed <= 0)
        {
            MessageBox.Show(this, "Aucun chrono à supprimer.", "Scores", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AfterMutation();
    }

    private void AfterMutation()
    {
        var keepTrackId = _tracks.Count > 0 ? _tracks[_currentIndex].TrackId : _preferredTrackId;
        _tracks = _board.GetTracksWithScores();
        _currentIndex = ResolveInitialIndex(keepTrackId);
        RefreshPlayerFilter(keepSelection: true);
        RefreshView();
        _controller?.NotifyScoresChanged();
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

    private static UIElement CreateHeader()
    {
        var grid = CreateGrid();
        AddCell(grid, 0, "#FFC5CAD3", "Rang", FontWeights.Bold);
        AddCell(grid, 1, "#FFC5CAD3", "Nom", FontWeights.Bold);
        AddCell(grid, 2, "#FFC5CAD3", "Temps", FontWeights.Bold, horizontalAlignment: HorizontalAlignment.Right);
        return grid;
    }

    private UIElement CreateRow(LeaderboardRow score)
    {
        var grid = CreateGrid();
        var rankColor = score.Rank switch
        {
            1 => "#FFFFD700",
            2 => "#FFC0C7D1",
            3 => "#FFE8A87C",
            _ => "#FFE10600",
        };
        AddCell(grid, 0, rankColor, $"{score.Rank}.", FontWeights.Bold);
        AddCell(grid, 1, "#FFFFFFFF", score.Name, FontWeights.SemiBold);
        AddCell(grid, 2, "#FFFFFFFF", score.FormattedTime, FontWeights.Bold, horizontalAlignment: HorizontalAlignment.Right);

        var delete = new Button
        {
            Content = "×",
            Width = 26,
            Height = 24,
            FontSize = 14,
            Padding = new Thickness(0),
            Tag = score,
            ToolTip = "Supprimer ce chrono",
            VerticalAlignment = VerticalAlignment.Center,
        };
        delete.Click += OnDeleteEntryClick;

        var menu = new ContextMenu();
        var deleteAll = new MenuItem
        {
            Header = $"Supprimer tous les chronos de {score.Name}",
            Tag = score.Name,
        };
        deleteAll.Click += OnDeletePlayerClick;
        menu.Items.Add(deleteAll);
        delete.ContextMenu = menu;

        Grid.SetColumn(delete, 3);
        grid.Children.Add(delete);

        grid.ContextMenu = menu;
        return grid;
    }

    private static Grid CreateGrid()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        return grid;
    }

    private static void AddCell(
        Grid grid,
        int column,
        string color,
        string text,
        FontWeight weight,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(column == 2 ? "Consolas" : "Segoe UI"),
            FontSize = 13,
            FontWeight = weight,
            Foreground = UiBrushes.FromHex(color),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        DragMove();
    }

    private sealed record SourceOption(string? ContestId, string Label);
}
