using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.Core.Models;
using Application = System.Windows.Application;

namespace MT_F1Chronos.App.Windows;

public partial class OverlayWindow : Window
{
    private readonly AppController _controller;
    private HwndSource? _hwndSource;

    public OverlayWindow(AppSettings settings, AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        Width = settings.OverlayWidth;

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
        _hwndSource.AddHook(WndProc);
        HotKeyHelper.Register(_hwndSource.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotKeyHelper.WmHotKey && wParam == (IntPtr)HotKeyHelper.NewSessionId)
        {
            _controller.PromptPlayerName();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_hwndSource is not null)
        {
            HotKeyHelper.Unregister(_hwndSource.Handle);
            _hwndSource.RemoveHook(WndProc);
        }
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (MenuButton.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = MenuButton;
            menu.IsOpen = true;
        }
    }

    private void OnRenameClick(object sender, RoutedEventArgs e) => _controller.PromptPlayerName();
    private void OnScoresClick(object sender, RoutedEventArgs e) => _controller.ShowAllScores();
    private void OnSizeSmallClick(object sender, RoutedEventArgs e) => _controller.SetOverlayWidth(OverlaySizes.Small);
    private void OnSizeMediumClick(object sender, RoutedEventArgs e) => _controller.SetOverlayWidth(OverlaySizes.Medium);
    private void OnSizeLargeClick(object sender, RoutedEventArgs e) => _controller.SetOverlayWidth(OverlaySizes.Large);
    private void OnQuitClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    public void UpdateSnapshot(OverlaySnapshot snapshot)
    {
        TrackText.Text = snapshot.TrackName.ToUpperInvariant();
        PlayerNameText.Text = snapshot.PlayerName;
        CurrentLapText.Text = snapshot.CurrentLapFormatted;
        BestLabelText.Text = "Meilleur";
        CurrentBestText.Text = snapshot.CurrentBestFormatted;

        TopFivePanel.Children.Clear();

        if (snapshot.TopFive.Count == 0)
        {
            TopFivePanel.Children.Add(CreateRow("—", "Aucun chrono", "--:--.---", false));
        }
        else
        {
            foreach (var row in snapshot.TopFive)
                TopFivePanel.Children.Add(CreateRow($"{row.Rank}.", row.Name, row.FormattedTime, true));
        }

        StatusText.Text = snapshot.IsConnected
            ? (snapshot.IsTimeTrial ? "Chrono actif" : snapshot.HasCurrentLap ? "Tour en cours" : "Connecté")
            : "En attente de F1 26…";
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
