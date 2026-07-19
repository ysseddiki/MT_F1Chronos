using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;
using Application = System.Windows.Application;

namespace MT_F1Chronos.App.Windows;

public partial class OverlayWindow : Window
{
    private const string RankGold = OverlayTheme.RankGold;
    private const string RankSilver = OverlayTheme.RankSilver;
    private const string RankBronze = OverlayTheme.RankBronze;
    private const string RankDefault = OverlayTheme.TextWhite;
    private const string StatusRed = OverlayTheme.StatusRed;
    private const string StatusBlue = OverlayTheme.StatusBlue;
    private const string StatusGreen = OverlayTheme.StatusGreen;
    private const string PlayerAccent = OverlayTheme.WarmRed;

    private readonly AppController _controller;
    private HwndSource? _hwndSource;
    private readonly DispatcherTimer _topMostTimer;
    private string _lastTrackName = string.Empty;
    private bool _leaderboardLayout;
    private bool _statusPulseActive;
    private string _widthFitKey = string.Empty;

    public OverlayWindow(AppSettings settings, AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        MinWidth = OverlaySizes.Default;
        MaxWidth = OverlaySizes.Max;
        Width = Math.Clamp(settings.OverlayWidth, OverlaySizes.Default, OverlaySizes.Max);
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
        LeaderboardTitleText.Text = $"{LeaderboardSizes.FormatLabel(snapshot.LeaderboardSize)} · GLOBAL";

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
            ContestTitleText.Text =
                $"{LeaderboardSizes.FormatLabel(snapshot.ContestLeaderboardSize)} · {label.ToUpperInvariant()}";
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

        // Only refit when width-relevant content changes — not every 250 ms refresh
        // (that was making the overlay crawl left/right by itself).
        var fitKey = BuildWidthFitKey(snapshot);
        if (!string.Equals(fitKey, _widthFitKey, StringComparison.Ordinal))
        {
            _widthFitKey = fitKey;
            FitWidthToContent();
        }
    }

    /// <summary>
    /// Compact default width; grow up to Max when content (long track/names) needs it.
    /// Keeps the right edge anchored. Measures without toggling SizeToContent (no jitter).
    /// </summary>
    private void FitWidthToContent()
    {
        MinWidth = OverlaySizes.Default;
        MaxWidth = OverlaySizes.Max;
        SizeToContent = SizeToContent.Height;

        RootBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Clamp(
            Math.Ceiling(RootBorder.DesiredSize.Width),
            OverlaySizes.Default,
            OverlaySizes.Max);

        if (Math.Abs(Width - width) < 1.0)
            return;

        var right = Left + Width;
        Width = width;
        Left = right - width;
    }

    private static string BuildWidthFitKey(OverlaySnapshot snapshot)
    {
        // Intentionally ignores live chrono / connection — those refresh often and
        // must not move the window.
        var sb = new System.Text.StringBuilder(128);
        sb.Append(snapshot.TrackName).Append('|')
          .Append(snapshot.PlayerName).Append('|')
          .Append(snapshot.LeaderboardSize).Append('|')
          .Append(snapshot.ContestLeaderboardSize).Append('|')
          .Append(snapshot.ShowGlobalLeaderboard).Append('|')
          .Append(snapshot.ShowContestLeaderboard).Append('|')
          .Append(snapshot.ContestLabel).Append('|');

        AppendBoardKey(sb, snapshot.Leaderboard);
        sb.Append('#');
        AppendBoardKey(sb, snapshot.ContestLeaderboard);
        return sb.ToString();
    }

    private static void AppendBoardKey(
        System.Text.StringBuilder sb,
        IReadOnlyList<LeaderboardRow> rows)
    {
        foreach (var row in rows)
        {
            sb.Append(row.Rank).Append(':')
              .Append(row.Name).Append(':')
              .Append(row.FormattedTime).Append(';');
        }
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
            FontFamily = new FontFamily(OverlayTheme.BodyFont),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiBrushes.FromHex(OverlayTheme.TextMuted),
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private static UIElement CreateRow(int rank, string name, string time, bool isCurrentPlayer)
    {
        const double rowFont = 13.5; // ~10% under previous 15

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Mockup: P1–P3 = colored text only (no wash). Current player = red row wash + red name only.
        var rankColor = RankColor(rank);
        var nameColor = isCurrentPlayer ? PlayerAccent : OverlayTheme.TextWhite;
        var timeColor = OverlayTheme.TextWhite;

        var rankElement = new TextBlock
        {
            Text = $"{rank}.",
            FontFamily = new FontFamily(OverlayTheme.MonoFont),
            FontSize = rowFont,
            FontWeight = FontWeights.Bold,
            Foreground = UiBrushes.FromHex(rankColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };

        var nameBlock = new TextBlock
        {
            Text = name,
            FontFamily = new FontFamily(OverlayTheme.BodyFont),
            FontSize = rowFont,
            FontWeight = isCurrentPlayer ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = UiBrushes.FromHex(nameColor),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };

        var timeBlock = new TextBlock
        {
            Text = time,
            FontFamily = new FontFamily(OverlayTheme.MonoFont),
            FontSize = rowFont,
            FontWeight = FontWeights.Bold,
            Foreground = UiBrushes.FromHex(timeColor),
            Margin = new Thickness(12, 0, 0, 0),
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

        // No horizontal padding — it was inflating FitWidthToContent for the highlighted row.
        grid.Margin = new Thickness(0);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xE1, 0x06, 0x00)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(0, 2, 0, 2),
            Child = grid,
            Margin = new Thickness(0, 0, 0, 5),
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
        StatusCore.Fill = brush;
        StatusBadge.ToolTip = tooltip;

        if (!_statusPulseActive)
        {
            StatusGlowBlur.Radius = snapshot.IsConnected ? 8 : 6;
            StatusGlow.Opacity = snapshot.IsConnected ? 0.7 : 0.45;
            StatusCore.Opacity = 1;
        }

        SetStatusPulse(pulse);
    }

    private void SetStatusPulse(bool enabled)
    {
        if (_statusPulseActive == enabled)
            return;

        _statusPulseActive = enabled;

        StatusGlowBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
        StatusGlow.BeginAnimation(OpacityProperty, null);

        if (!enabled)
        {
            StatusGlowBlur.Radius = 8;
            StatusGlow.Opacity = 0.7;
            StatusCore.Opacity = 1;
            return;
        }

        // Soft breathe — glow only, no hard contour.
        var blurPulse = new DoubleAnimation
        {
            From = 6,
            To = 12,
            Duration = TimeSpan.FromMilliseconds(1100),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var glowOpacity = new DoubleAnimation
        {
            From = 0.45,
            To = 0.85,
            Duration = TimeSpan.FromMilliseconds(1100),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };

        StatusGlowBlur.BeginAnimation(BlurEffect.RadiusProperty, blurPulse);
        StatusGlow.BeginAnimation(OpacityProperty, glowOpacity);
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
