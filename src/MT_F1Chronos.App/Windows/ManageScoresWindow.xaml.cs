using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.App.ViewModels;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;

namespace MT_F1Chronos.App.Windows;

public partial class ManageScoresWindow : Window
{
    private const string AllPlayersLabel = "Tous les joueurs";

    private readonly BoardSourceViewModel _source;
    private readonly SessionStore _globalStore;
    private readonly ContestStore _contests;
    private readonly AppController _controller;
    private readonly int? _preferredTrackId;

    private IScoreBoardView _board;
    private string? _contestId;
    private IReadOnlyList<TrackSummary> _tracks = [];
    private int _currentIndex;
    private bool _ready;
    private bool _bestPerPlayer;
    private string? _playerFilter;

    public ManageScoresWindow(
        SessionStore globalStore,
        ContestStore contests,
        AppController controller,
        int? initialTrackId = null)
    {
        _globalStore = globalStore;
        _contests = contests;
        _source = new BoardSourceViewModel(globalStore, contests);
        _controller = controller;
        _preferredTrackId = initialTrackId;
        _bestPerPlayer = controller.GetBestPerPlayer();
        _board = _source.Board;

        InitializeComponent();
        SyncBestPerPlayerButton();
        PopulateSources();
        _ready = true;
        ApplySelectedSource(keepTrackId: initialTrackId);
    }

    private void PopulateSources()
    {
        SourceCombo.Items.Clear();
        foreach (var option in _source.ListSources())
            SourceCombo.Items.Add(option);

        SourceCombo.DisplayMemberPath = nameof(BoardSourceOption.Label);
        SourceCombo.SelectedIndex = 0;
    }

    private void ApplySelectedSource(int? keepTrackId)
    {
        var option = SourceCombo.SelectedItem as BoardSourceOption;
        _source.Select(option?.ContestId);
        _board = _source.Board;
        _contestId = _source.ContestId;

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
        SyncActionButtons();

        if (_tracks.Count == 0)
        {
            TrackTitleText.Text = "Aucun";
            PrevTrackButton.IsEnabled = false;
            NextTrackButton.IsEnabled = false;
            ListHintText.Text = "Aucun chrono dans ce tableau.";
            ContextHintText.Text = $"Tableau : {_board.BoardLabel}";
            ScoresPanel.Children.Add(CreateMessage("Rien à afficher pour cette source."));
            return;
        }

        var track = _tracks[_currentIndex];
        TrackTitleText.Text = track.TrackName;
        PrevTrackButton.IsEnabled = _tracks.Count > 1;
        NextTrackButton.IsEnabled = _tracks.Count > 1;

        var scores = _board.GetScoresForTrack(track.TrackId, _bestPerPlayer, _playerFilter);
        var mode = _bestPerPlayer ? " · meilleur / joueur" : string.Empty;
        var player = string.IsNullOrWhiteSpace(_playerFilter) ? string.Empty : $" · {_playerFilter}";
        ListHintText.Text =
            $"{_board.BoardLabel} · {track.TrackName} · {scores.Count} ligne(s){mode}{player}  ·  × = supprimer ce chrono";
        ContextHintText.Text =
            $"Les actions ci-dessus s’appliquent au tableau « {_board.BoardLabel} » uniquement.";

        if (scores.Count == 0)
        {
            ScoresPanel.Children.Add(CreateMessage("Aucun chrono pour ce filtre."));
            return;
        }

        ScoresPanel.Children.Add(LeaderboardRowUi.CreateHeader(includeDeleteColumn: true));
        foreach (var score in scores)
            ScoresPanel.Children.Add(LeaderboardRowUi.CreateRow(score, OnDeleteEntryClick));
    }

    private void SyncActionButtons()
    {
        var hasTrack = _tracks.Count > 0;
        var hasPlayer = !string.IsNullOrWhiteSpace(_playerFilter);
        DeletePlayerButton.IsEnabled = hasTrack && hasPlayer;
        DeletePlayerButton.Content = hasPlayer
            ? $"Supprimer « {_playerFilter} »…"
            : "Supprimer le joueur…";
        ClearTrackButton.IsEnabled = hasTrack;
        ClearTrackButton.Content = hasTrack
            ? $"Effacer « {_tracks[_currentIndex].TrackName} »…"
            : "Effacer ce circuit…";

        ClearBoardButton.Content = _contestId is null
            ? "Vider tous les scores globaux…"
            : $"Vider entièrement « {_board.BoardLabel} »…";
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

        var keep = _tracks.Count > 0 ? _tracks[_currentIndex].TrackId : _preferredTrackId;
        ApplySelectedSource(keep);
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

        if (!Confirm(
                $"Supprimer le chrono {row.FormattedTime} de {row.Name} ?",
                "Supprimer un chrono"))
            return;

        if (!_board.DeleteEntry(row.EntryId))
        {
            MessageBox.Show(this, "Impossible de supprimer ce chrono.", "Gérer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AfterMutation();
    }

    private void OnDeletePlayerClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_playerFilter) || _tracks.Count == 0)
            return;

        var track = _tracks[_currentIndex];
        if (!Confirm(
                $"Supprimer tous les chronos de « {_playerFilter} » sur {track.TrackName}\n" +
                $"(tableau : {_board.BoardLabel}) ?",
                "Supprimer un joueur"))
            return;

        var removed = _board.DeletePlayerOnTrack(_playerFilter, track.TrackId);
        MessageBox.Show(this, $"{removed} chrono(s) supprimé(s).", "Gérer", MessageBoxButton.OK, MessageBoxImage.Information);
        AfterMutation();
    }

    private void OnClearTrackClick(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0)
            return;

        var track = _tracks[_currentIndex];
        if (!Confirm(
                $"Effacer tous les chronos de {track.TrackName}\n" +
                $"dans le tableau « {_board.BoardLabel} » ?\n\nIrréversible.",
                "Effacer le circuit"))
            return;

        var removed = _board.ClearTrack(track.TrackId);
        MessageBox.Show(this, $"{removed} chrono(s) supprimé(s).", "Gérer", MessageBoxButton.OK, MessageBoxImage.Information);
        AfterMutation();
    }

    private void OnClearBoardClick(object sender, RoutedEventArgs e)
    {
        if (!Confirm(
                $"Vider entièrement le tableau « {_board.BoardLabel} » ?\n" +
                "Tous les circuits de ce tableau seront effacés.\n\nIrréversible.",
                "Vider le tableau"))
            return;

        var removed = _board.ClearAll();
        MessageBox.Show(this, $"{removed} chrono(s) supprimé(s).", "Gérer", MessageBoxButton.OK, MessageBoxImage.Information);
        AfterMutation();
    }

    private void OnFullResetClick(object sender, RoutedEventArgs e)
    {
        if (!Confirm(
                "Réinitialisation complète :\n" +
                "• tous les scores globaux\n" +
                "• tous les scores de tous les concours\n\n" +
                "Les concours eux-mêmes sont conservés.\nIrréversible.",
                "Réinitialisation complète"))
            return;

        var globalRemoved = _globalStore.ClearAllScores();
        var contestRemoved = 0;
        foreach (var contest in _contests.List())
            contestRemoved += _contests.ClearAllScores(contest.Id);

        MessageBox.Show(
            this,
            $"{globalRemoved} chrono(s) global(aux) + {contestRemoved} chrono(s) concours supprimé(s).",
            "Gérer",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        AfterMutation();
    }

    private void AfterMutation()
    {
        var keep = _tracks.Count > 0 ? _tracks[_currentIndex].TrackId : _preferredTrackId;
        _tracks = _board.GetTracksWithScores();
        _currentIndex = ResolveInitialIndex(keep);
        RefreshPlayerFilter(keepSelection: true);
        RefreshView();
        _controller.NotifyScoresChanged();
    }

    private bool Confirm(string message, string title) =>
        MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) ==
        MessageBoxResult.Yes;

    private static UIElement CreateMessage(string text) =>
        new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Foreground = UiBrushes.FromHex("#88FFFFFF"),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        DragMove();
    }
}
