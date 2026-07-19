using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;

namespace MT_F1Chronos.App.Windows;

public partial class ScoresWindow : Window
{
    private readonly SessionStore _globalStore;
    private readonly ContestStore _contests;
    private readonly int? _preferredTrackId;

    private IScoreBoardView _board;
    private IReadOnlyList<TrackSummary> _tracks = [];
    private int _currentIndex;
    private bool _ready;

    public ScoresWindow(
        SessionStore globalStore,
        ContestStore contests,
        int? initialTrackId = null,
        string? initialContestId = null)
    {
        _globalStore = globalStore;
        _contests = contests;
        _preferredTrackId = initialTrackId;

        InitializeComponent();
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

    private void RefreshView()
    {
        ScoresPanel.Children.Clear();

        if (_tracks.Count == 0)
        {
            TrackTitleText.Text = "Aucun circuit";
            TrackIndexText.Text = string.Empty;
            PrevTrackButton.IsEnabled = false;
            NextTrackButton.IsEnabled = false;
            ScoresPanel.Children.Add(CreateMessage("Aucun score enregistré."));
            return;
        }

        var track = _tracks[_currentIndex];
        TrackTitleText.Text = track.TrackName;
        TrackIndexText.Text = $"Circuit {_currentIndex + 1} / {_tracks.Count} · {track.ScoreCount} chrono(s)";
        PrevTrackButton.IsEnabled = _tracks.Count > 1;
        NextTrackButton.IsEnabled = _tracks.Count > 1;

        var scores = _board.GetScoresForTrack(track.TrackId);
        if (scores.Count == 0)
        {
            ScoresPanel.Children.Add(CreateMessage("Aucun score pour ce circuit."));
            return;
        }

        ScoresPanel.Children.Add(CreateHeader());

        foreach (var score in scores)
            ScoresPanel.Children.Add(CreateRow(score));
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;

        var keepTrackId = _tracks.Count > 0 ? _tracks[_currentIndex].TrackId : _preferredTrackId;
        ApplySelectedSource(keepTrackId);
    }

    private void OnPrevTrackClick(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count <= 1)
            return;

        _currentIndex = (_currentIndex - 1 + _tracks.Count) % _tracks.Count;
        RefreshView();
    }

    private void OnNextTrackClick(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count <= 1)
            return;

        _currentIndex = (_currentIndex + 1) % _tracks.Count;
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
        };

    private static UIElement CreateHeader()
    {
        var grid = CreateGrid();
        AddCell(grid, 0, "#FFC5CAD3", "Rang", FontWeights.Bold);
        AddCell(grid, 1, "#FFC5CAD3", "Nom", FontWeights.Bold);
        AddCell(grid, 2, "#FFC5CAD3", "Temps", FontWeights.Bold, horizontalAlignment: HorizontalAlignment.Right);
        return grid;
    }

    private static UIElement CreateRow(LeaderboardRow score)
    {
        var grid = CreateGrid();
        AddCell(grid, 0, "#FFE10600", $"{score.Rank}.", FontWeights.Bold);
        AddCell(grid, 1, "#FFFFFFFF", score.Name, FontWeights.SemiBold);
        AddCell(grid, 2, "#FFFFFFFF", score.FormattedTime, FontWeights.Bold, horizontalAlignment: HorizontalAlignment.Right);
        return grid;
    }

    private static Grid CreateGrid()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
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
