using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;
using Application = System.Windows.Application;

namespace MT_F1Chronos.App.Windows;

public partial class OverlayWindow : Window
{
    private const string RankGold = "#FFFFD700";
    private const string RankSilver = "#FFC0C7D1";
    private const string RankBronze = "#FFE8A87C";
    private const string RankDefault = "#FFE10600";
    private const string StatusRed = "#FFE10600";
    private const string StatusBlue = "#FF5E8BFF";
    private const string StatusGreen = "#FF00D26A";

    private readonly AppController _controller;
    private HwndSource? _hwndSource;
    private readonly DispatcherTimer _topMostTimer;
    private string _lastTrackName = string.Empty;
    private bool _leaderboardLayout;
    private bool _statusPulseActive;

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
    private void OnScoresClick(object sender, RoutedEventArgs e) => _controller.ShowAllScores();
    private void OnAdminClick(object sender, RoutedEventArgs e) => _controller.ShowAdminWindow();
    private void OnQuitClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    public void UpdateLiveChrono(uint? currentLapMs)
    {
        var text = currentLapMs is > 0
            ? LapTimeFormatter.Format(currentLapMs.Value)
            : "--:--.---";

        if (string.Equals(CurrentLapText.Text, text, StringComparison.Ordinal))
            return;

        CurrentLapText.Text = text;
    }

    public void UpdateSnapshot(OverlaySnapshot snapshot)
    {
        var trackChanged = !string.Equals(_lastTrackName, snapshot.TrackName, StringComparison.Ordinal);
        var layout = snapshot.ShowGlobalLeaderboard || snapshot.ShowContestLeaderboard;
        var layoutChanged = layout != _leaderboardLayout;
        _lastTrackName = snapshot.TrackName;
        _leaderboardLayout = layout;

        TrackText.Text = snapshot.TrackName.ToUpperInvariant();
        PlayerNameText.Text = snapshot.PlayerName;
        CurrentLapText.Text = snapshot.CurrentLapFormatted;
        LeaderboardTitleText.Text = snapshot.LeaderboardSize == LeaderboardSizes.Extended
            ? "TOP 10 · GLOBAL"
            : "TOP 5 · GLOBAL";

        if (snapshot.ShowGlobalLeaderboard)
        {
            GlobalSection.Visibility = Visibility.Visible;
            FillLeaderboardPanel(TopFivePanel, snapshot.Leaderboard, snapshot.PlayerName);
        }
        else
        {
            GlobalSection.Visibility = Visibility.Collapsed;
            TopFivePanel.Children.Clear();
        }

        if (snapshot.ShowContestLeaderboard)
        {
            ContestSection.Visibility = Visibility.Visible;
            ContestDivider.Visibility = snapshot.ShowGlobalLeaderboard
                ? Visibility.Visible
                : Visibility.Collapsed;
            var label = string.IsNullOrWhiteSpace(snapshot.ContestLabel) ? "CONCOURS" : snapshot.ContestLabel;
            ContestTitleText.Text = snapshot.ContestLeaderboardSize == LeaderboardSizes.Extended
                ? $"TOP 10 · {label.ToUpperInvariant()}"
                : $"TOP 5 · {label.ToUpperInvariant()}";
            FillLeaderboardPanel(ContestPanel, snapshot.ContestLeaderboard, snapshot.PlayerName);
        }
        else
        {
            ContestSection.Visibility = Visibility.Collapsed;
            ContestDivider.Visibility = Visibility.Collapsed;
            ContestPanel.Children.Clear();
        }

        UpdateStatusSquare(snapshot);

        if (trackChanged || layoutChanged)
            PlayContentFade();
    }

    /// <summary>Brief highlight when a lap is recorded for the current player.</summary>
    public void FlashLapRecorded(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return;

        foreach (var panel in new[] { TopFivePanel, ContestPanel })
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is not FrameworkElement fe)
                    continue;
                if (!string.Equals(fe.Tag as string, playerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                PlayRowPulse(fe);
            }
        }
    }

    private static void FillLeaderboardPanel(
        StackPanel panel,
        IReadOnlyList<LeaderboardRow> rows,
        string playerName)
    {
        panel.Children.Clear();

        if (rows.Count == 0)
        {
            panel.Children.Add(CreateEmptyRow());
            return;
        }

        foreach (var row in rows)
        {
            var isCurrent = string.Equals(row.Name, playerName, StringComparison.OrdinalIgnoreCase);
            panel.Children.Add(CreateRow(row.Rank, row.Name, row.FormattedTime, isCurrent));
        }
    }

    private static UIElement CreateEmptyRow()
    {
        return new TextBlock
        {
            Text = "Aucun chrono — lance un tour valide",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiBrushes.FromHex("#66FFFFFF"),
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private static UIElement CreateRow(int rank, string name, string time, bool isCurrentPlayer)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rankColor = RankColor(rank);

        // Rank cell: podium wash for P1–P3, unless this is the current player row.
        UIElement rankElement;
        if (!isCurrentPlayer && rank is >= 1 and <= 3)
        {
            var (r, g, b) = RankWashRgb(rank);
            rankElement = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x28, r, g, b)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(2, 1, 2, 1),
                Child = new TextBlock
                {
                    Text = $"{rank}.",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = UiBrushes.FromHex(rankColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }
        else
        {
            rankElement = new TextBlock
            {
                Text = $"{rank}.",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = UiBrushes.FromHex(rankColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var nameBlock = new TextBlock
        {
            Text = name,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            FontWeight = isCurrentPlayer ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = UiBrushes.FromHex("#FFFFFFFF"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var timeBlock = new TextBlock
        {
            Text = time,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = UiBrushes.FromHex("#FFFFFFFF"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(rankElement, 0);
        Grid.SetColumn(nameBlock, 1);
        Grid.SetColumn(timeBlock, 2);

        grid.Children.Add(rankElement);
        grid.Children.Add(nameBlock);
        grid.Children.Add(timeBlock);

        if (!isCurrentPlayer)
        {
            grid.Tag = name;
            return grid;
        }

        // Current player: red wash wins over podium wash.
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xE1, 0x06, 0x00)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 3, 4, 3),
            Child = grid,
            Margin = new Thickness(0, 0, 0, 2),
            Tag = name,
        };
    }

    private static string RankColor(int rank) => rank switch
    {
        1 => RankGold,
        2 => RankSilver,
        3 => RankBronze,
        _ => RankDefault,
    };

    private static (byte R, byte G, byte B) RankWashRgb(int rank) => rank switch
    {
        1 => (0xFF, 0xD7, 0x00),
        2 => (0xC0, 0xC7, 0xD1),
        3 => (0xE8, 0xA8, 0x7C),
        _ => (0xE1, 0x06, 0x00),
    };
    private void UpdateStatusSquare(OverlaySnapshot snapshot)
    {
        // Red = no telemetry · Blue = connected, waiting for CLM · Green = CLM running
        Color accent;
        string tooltip;
        var pulse = false;

        if (!snapshot.IsConnected)
        {
            accent = (Color)ColorConverter.ConvertFromString(StatusRed)!;
            tooltip = "Télémétrie absente";
        }
        else if (snapshot.HasCurrentLap)
        {
            accent = (Color)ColorConverter.ConvertFromString(StatusGreen)!;
            tooltip = "Tour en cours";
            pulse = true;
        }
        else
        {
            accent = (Color)ColorConverter.ConvertFromString(StatusBlue)!;
            tooltip = "Connecté — en attente d’un tour";
        }

        var brush = new SolidColorBrush(accent);
        brush.Freeze();

        StatusGlow.Fill = brush;
        StatusCore.Background = brush;
        StatusRing.BorderBrush = brush;
        StatusCoreGlow.Color = accent;
        StatusBadge.ToolTip = tooltip;

        // Idle glow strength: softer when disconnected, fuller when connected.
        if (!_statusPulseActive)
        {
            StatusGlowBlur.Radius = snapshot.IsConnected ? 11 : 8;
            StatusCoreGlow.BlurRadius = snapshot.IsConnected ? 10 : 7;
            StatusCoreGlow.Opacity = snapshot.IsConnected ? 0.95 : 0.7;
            StatusGlow.Opacity = snapshot.IsConnected ? 0.85 : 0.55;
        }

        SetStatusPulse(pulse);
    }

    private void SetStatusPulse(bool enabled)
    {
        if (_statusPulseActive == enabled)
            return;

        _statusPulseActive = enabled;

        StatusGlowBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
        StatusCoreGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        StatusCoreGlow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        StatusGlow.BeginAnimation(OpacityProperty, null);

        if (!enabled)
        {
            StatusGlowBlur.Radius = 11;
            StatusCoreGlow.BlurRadius = 10;
            StatusCoreGlow.Opacity = 0.95;
            StatusGlow.Opacity = 0.85;
            return;
        }

        // Breathe the soft glow outward (dissipation) while CLM is running.
        var blurPulse = new DoubleAnimation
        {
            From = 8,
            To = 16,
            Duration = TimeSpan.FromMilliseconds(1100),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var coreBlurPulse = new DoubleAnimation
        {
            From = 8,
            To = 14,
            Duration = TimeSpan.FromMilliseconds(1100),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var glowOpacity = new DoubleAnimation
        {
            From = 0.55,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(1100),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var shadowOpacity = new DoubleAnimation
        {
            From = 0.65,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(1100),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };

        StatusGlowBlur.BeginAnimation(BlurEffect.RadiusProperty, blurPulse);
        StatusCoreGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, coreBlurPulse);
        StatusGlow.BeginAnimation(OpacityProperty, glowOpacity);
        StatusCoreGlow.BeginAnimation(DropShadowEffect.OpacityProperty, shadowOpacity);
    }

    private void PlayContentFade()
    {
        var animation = new DoubleAnimation
        {
            From = 0.55,
            To = 0.96,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        RootBorder.BeginAnimation(OpacityProperty, animation);
    }

    private static void PlayRowPulse(FrameworkElement element)
    {
        var animation = new DoubleAnimation
        {
            From = 0.45,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        element.BeginAnimation(OpacityProperty, animation);
    }
}
