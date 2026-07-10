using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MT_F1Chronos.App.Native;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.App.Windows;

public partial class OverlayWindow : Window
{
    public OverlayWindow(AppSettings settings)
    {
        InitializeComponent();
        Width = settings.OverlayWidth;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ClickThroughHelper.EnableClickThrough(this);
    }

    public void UpdateSnapshot(OverlaySnapshot snapshot)
    {
        TrackText.Text = snapshot.TrackName.ToUpperInvariant();
        SessionNameText.Text = snapshot.CurrentSessionName;
        CurrentBestText.Text = snapshot.CurrentBestFormatted;

        TopThreePanel.Children.Clear();

        if (snapshot.TopThree.Count == 0)
        {
            TopThreePanel.Children.Add(CreateRow("—", "Aucun chrono", "--:--.---", false));
        }
        else
        {
            foreach (var row in snapshot.TopThree)
                TopThreePanel.Children.Add(CreateRow($"{row.Rank}.", row.Name, row.FormattedTime, true));
        }

        StatusText.Text = snapshot.IsConnected
            ? (snapshot.IsTimeTrial ? "Chrono actif" : "Connecté")
            : "En attente de F1 25…";
    }

    private static UIElement CreateRow(string rank, string name, string time, bool hasData)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rankColor = hasData ? "#FFE10600" : "#66FFFFFF";
        var nameColor = hasData ? "#FFFFFFFF" : "#88FFFFFF";
        var timeColor = hasData ? "#FFFFD700" : "#66FFFFFF";

        var rankBlock = new TextBlock
        {
            Text = rank,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(rankColor)!),
        };

        var nameBlock = new TextBlock
        {
            Text = name,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(nameColor)!),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var timeBlock = new TextBlock
        {
            Text = time,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(timeColor)!),
            Margin = new Thickness(8, 0, 0, 0),
        };

        Grid.SetColumn(rankBlock, 0);
        Grid.SetColumn(nameBlock, 1);
        Grid.SetColumn(timeBlock, 2);

        grid.Children.Add(rankBlock);
        grid.Children.Add(nameBlock);
        grid.Children.Add(timeBlock);

        return grid;
    }
}
