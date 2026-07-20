using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.App;

/// <summary>Shared leaderboard row / header builders for Scores and ManageScores.</summary>
internal static class LeaderboardRowUi
{
    public static UIElement CreateHeader(bool includeDeleteColumn = false)
    {
        var grid = CreateGrid(includeDeleteColumn);
        AddCell(grid, 0, "#FFC5CAD3", "Rang", FontWeights.Bold);
        AddCell(grid, 1, "#FFC5CAD3", "Nom", FontWeights.Bold);
        AddCell(grid, 2, "#FFC5CAD3", "Temps", FontWeights.Bold, HorizontalAlignment.Right);
        return grid;
    }

    public static UIElement CreateRow(LeaderboardRow score, RoutedEventHandler? onDeleteClick = null)
    {
        var includeDelete = onDeleteClick is not null;
        var grid = CreateGrid(includeDelete);
        var rankColor = score.Rank switch
        {
            1 => "#FFFFD700",
            2 => "#FFC0C7D1",
            3 => "#FFE8A87C",
            _ => "#FFE10600",
        };
        AddCell(grid, 0, rankColor, $"{score.Rank}.", FontWeights.Bold);
        AddCell(grid, 1, "#FFFFFFFF", score.Name, FontWeights.SemiBold);
        AddCell(grid, 2, "#FFFFFFFF", score.FormattedTime, FontWeights.Bold, HorizontalAlignment.Right);

        if (includeDelete)
        {
            var delete = new Button
            {
                Content = "×",
                Width = 28,
                Height = 26,
                FontSize = 14,
                Padding = new Thickness(0),
                Margin = new Thickness(10, 0, 0, 0),
                Tag = score,
                ToolTip = "Supprimer ce chrono",
                VerticalAlignment = VerticalAlignment.Center,
            };
            delete.Click += onDeleteClick;
            Grid.SetColumn(delete, 3);
            grid.Children.Add(delete);
        }

        return grid;
    }

    private static Grid CreateGrid(bool includeDeleteColumn)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, includeDeleteColumn ? 6 : 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        if (includeDeleteColumn)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
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
}
