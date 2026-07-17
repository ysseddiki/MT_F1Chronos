using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.Core.Models;
using Application = System.Windows.Application;

namespace MT_F1Chronos.App.Windows;

public partial class OverlayWindow : Window
{
    private readonly AppController _controller;
    private HwndSource? _hwndSource;
    private readonly DispatcherTimer _topMostTimer;

    public OverlayWindow(AppSettings settings, AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        Width = settings.OverlayWidth;
        Topmost = true;

        SourceInitialized += OnSourceInitialized;
        Activated += (_, _) => AssertTopMost();
        Deactivated += (_, _) => AssertTopMost();
        Closed += OnClosed;

        _topMostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topMostTimer.Tick += (_, _) => AssertTopMost();
        _topMostTimer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
        _hwndSource.AddHook(WndProc);
        HotKeyHelper.Register(_hwndSource.Handle);
        AssertTopMost();
    }

    private void AssertTopMost()
    {
        Topmost = true;
        if (_hwndSource is not null)
            TopMostHelper.Assert(_hwndSource.Handle);
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
        _topMostTimer.Stop();

        if (_hwndSource is not null)
        {
            HotKeyHelper.Unregister(_hwndSource.Handle);
            _hwndSource.RemoveHook(WndProc);
        }
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        DragMove();
        _controller.SaveOverlayPosition(Left, Top, Width);
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
    private void OnAdminClick(object sender, RoutedEventArgs e) => _controller.ShowAdminWindow();
    private void OnQuitClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    public void UpdateSnapshot(OverlaySnapshot snapshot)
    {
        TrackText.Text = snapshot.TrackName.ToUpperInvariant();
        PlayerNameText.Text = snapshot.PlayerName;
        CurrentLapText.Text = snapshot.CurrentLapFormatted;
        LeaderboardTitleText.Text = snapshot.LeaderboardSize == LeaderboardSizes.Extended ? "TOP 10" : "TOP 5";

        TopFivePanel.Children.Clear();

        if (snapshot.Leaderboard.Count == 0)
        {
            TopFivePanel.Children.Add(CreateRow("—", "Aucun chrono", "--:--.---", false, false));
        }
        else
        {
            foreach (var row in snapshot.Leaderboard)
            {
                var isCurrent = string.Equals(row.Name, snapshot.PlayerName, StringComparison.OrdinalIgnoreCase);
                TopFivePanel.Children.Add(CreateRow($"{row.Rank}.", row.Name, row.FormattedTime, true, isCurrent));
            }
        }

        UpdateStatusBar(snapshot);
    }

    private static UIElement CreateRow(string rank, string name, string time, bool hasData, bool isCurrentPlayer)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rankColor = hasData ? "#FFE10600" : "#55FFFFFF";
        var nameColor = hasData
            ? (isCurrentPlayer ? "#FFFFD700" : "#FFFFFFFF")
            : "#77FFFFFF";
        var timeColor = hasData ? "#FFFFFFFF" : "#55FFFFFF";

        UIElement content = grid;

        if (isCurrentPlayer && hasData)
        {
            var highlight = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xE1, 0x06, 0x00)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Child = grid,
                Margin = new Thickness(0, 0, 0, 2),
            };
            content = highlight;
            grid.Margin = new Thickness(0);
        }

        var rankBlock = new TextBlock
        {
            Text = rank,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = UiBrushes.FromHex(rankColor),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var nameBlock = new TextBlock
        {
            Text = name,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = isCurrentPlayer ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = UiBrushes.FromHex(nameColor),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var timeBlock = new TextBlock
        {
            Text = time,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = UiBrushes.FromHex(timeColor),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(rankBlock, 0);
        Grid.SetColumn(nameBlock, 1);
        Grid.SetColumn(timeBlock, 2);

        grid.Children.Add(rankBlock);
        grid.Children.Add(nameBlock);
        grid.Children.Add(timeBlock);

        return content;
    }

    private void UpdateStatusBar(OverlaySnapshot snapshot)
    {
        if (!snapshot.IsConnected)
        {
            SetStatusStyle("En attente de F1…", "▱", "#FFEA9A00", "#FFC5CAD3", "#66EA9A00");
            return;
        }

        if (snapshot.IsTimeTrial || snapshot.HasCurrentLap)
        {
            SetStatusStyle("Tour en cours", "◉", "#FF5E8BFF", "#FF7FA4FF", "#665E8BFF");
            return;
        }

        SetStatusStyle("Connecté", "▮▮▮", "#FF00D26A", "#FF77E3A8", "#6600D26A");
    }

    private void SetStatusStyle(string text, string icon, string accentColor, string textColor, string borderColor)
    {
        StatusText.Text = text;
        StatusIconText.Text = icon;
        StatusDot.Fill = UiBrushes.FromHex(accentColor);
        StatusText.Foreground = UiBrushes.FromHex(textColor);
        StatusIconText.Foreground = UiBrushes.FromHex(accentColor);
        StatusBorder.BorderBrush = UiBrushes.FromHex(borderColor);
    }
}
