using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;

namespace MT_F1Chronos.App.Windows;

public partial class ScoresWindow : Window
{
    private readonly SessionStore _store;
    private readonly IReadOnlyList<TrackSummary> _tracks;
    private int _currentIndex;

    public ScoresWindow(SessionStore store, int? initialTrackId = null)
    {
        _store = store;
        _tracks = store.GetTracksWithScores();
        _currentIndex = ResolveInitialIndex(initialTrackId);

        InitializeComponent();
        RefreshView();
    }

    private int ResolveInitialIndex(int? initialTrackId)
    {
        if (_tracks.Count == 0)
            return 0;

        if (initialTrackId.HasValue)
        {
            var index = -1;
            for (var i = 0; i < _tracks.Count; i++)
            {
                if (_tracks[i].TrackId == initialTrackId.Value)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                return index;
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

        var scores = _store.GetScoresForTrack(track.TrackId);
        if (scores.Count == 0)
        {
            ScoresPanel.Children.Add(CreateMessage("Aucun score pour ce circuit."));
            return;
        }

        ScoresPanel.Children.Add(CreateHeader());

        foreach (var score in scores)
            ScoresPanel.Children.Add(CreateRow(score));
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
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88FFFFFF")!),
            Margin = new Thickness(0, 8, 0, 0),
        };

    private static UIElement CreateHeader()
    {
        var grid = CreateGrid();
        AddCell(grid, 0, "#FFB0B8C8", "Rang", FontWeights.Bold);
        AddCell(grid, 1, "#FFB0B8C8", "Nom", FontWeights.Bold);
        AddCell(grid, 2, "#FFB0B8C8", "Temps", FontWeights.Bold, horizontalAlignment: HorizontalAlignment.Right);
        return grid;
    }

    private static UIElement CreateRow(LeaderboardRow score)
    {
        var grid = CreateGrid();
        AddCell(grid, 0, "#FFE10600", $"{score.Rank}.", FontWeights.Bold);
        AddCell(grid, 1, "#FFFFFFFF", score.Name, FontWeights.Normal);
        AddCell(grid, 2, "#FFFFD700", score.FormattedTime, FontWeights.Bold, horizontalAlignment: HorizontalAlignment.Right);
        return grid;
    }

    private static Grid CreateGrid()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
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
            FontSize = 11,
            FontWeight = weight,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = horizontalAlignment,
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
