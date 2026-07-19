using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.App.Windows;

public partial class AdminWindow : Window
{
    private static readonly SolidColorBrush AccentBorder = CreateBrush("#88E10600");
    private static readonly SolidColorBrush DefaultBorder = CreateBrush("#33FFFFFF");
    private static readonly SolidColorBrush SelectedBg = CreateBrush("#FF1C222B");
    private static readonly SolidColorBrush DefaultBg = CreateBrush("#FF161B22");
    private static readonly SolidColorBrush CardBg = CreateBrush("#B2161B22");
    private static readonly SolidColorBrush Muted = CreateBrush("#FFC5CAD3");
    private static readonly SolidColorBrush White = CreateBrush("#FFFFFFFF");
    private static readonly SolidColorBrush Green = CreateBrush("#FF00D26A");
    private static readonly SolidColorBrush Amber = CreateBrush("#FFEA9A00");
    private static readonly SolidColorBrush Gray = CreateBrush("#FF8899AA");

    private readonly AppController _controller;
    private readonly AppSettings _settings;

    public AdminWindow(AppController controller, AppSettings settings)
    {
        _controller = controller;
        _settings = settings;
        InitializeComponent();
        SyncDisplayButtons();
        RefreshContestList();
    }

    private void SyncDisplayButtons()
    {
        var isTop10 = _settings.LeaderboardSize == LeaderboardSizes.Extended;
        Highlight(Top5Button, !isTop10);
        Highlight(Top10Button, isTop10);

        Highlight(SizeSmallButton, Math.Abs(_settings.OverlayWidth - OverlaySizes.Small) < 0.5);
        Highlight(SizeMediumButton, Math.Abs(_settings.OverlayWidth - OverlaySizes.Medium) < 0.5);
        Highlight(SizeLargeButton, Math.Abs(_settings.OverlayWidth - OverlaySizes.Large) < 0.5);

        Highlight(GlobalSourceButton, string.IsNullOrWhiteSpace(_settings.OverlayContestId));
    }

    private void RefreshContestList()
    {
        ContestListPanel.Children.Clear();
        var contests = _controller.ListContests();
        ContestEmptyText.Visibility = contests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var contest in contests)
            ContestListPanel.Children.Add(CreateContestCard(contest));

        SyncDisplayButtons();
    }

    private UIElement CreateContestCard(Contest contest)
    {
        var card = new Border
        {
            Background = CardBg,
            BorderBrush = DefaultBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var stack = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBlock = new TextBlock
        {
            Text = contest.Name,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameBlock, 0);
        header.Children.Add(nameBlock);

        var statusBlock = new TextBlock
        {
            Text = StatusLabel(contest.Status),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = StatusBrush(contest.Status),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(statusBlock, 1);
        header.Children.Add(statusBlock);
        stack.Children.Add(header);

        var count = _controller.GetContestEntryCount(contest.Id);
        var showing = string.Equals(_settings.OverlayContestId, contest.Id, StringComparison.Ordinal);
        stack.Children.Add(new TextBlock
        {
            Text = showing
                ? $"{count} chrono(s) · affiché sur l’overlay"
                : $"{count} chrono(s)",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            Foreground = Muted,
            Margin = new Thickness(0, 2, 0, 8),
        });

        var actions = new WrapPanel();

        if (contest.Status != ContestStatus.Active)
            actions.Children.Add(MakeAction("Démarrer", () =>
            {
                _controller.StartContest(contest.Id);
                RefreshContestList();
            }));

        if (contest.Status == ContestStatus.Active)
            actions.Children.Add(MakeAction("Arrêter", () =>
            {
                _controller.StopContest(contest.Id);
                RefreshContestList();
            }));

        actions.Children.Add(MakeAction(showing ? "Sur overlay ✓" : "Afficher", () =>
        {
            _controller.SetOverlayContest(contest.Id);
            RefreshContestList();
        }, highlight: showing));

        actions.Children.Add(MakeAction("Voir", () => _controller.ShowContestScores(contest.Id)));
        actions.Children.Add(MakeAction("CSV", () => _controller.ExportContestScores(contest.Id, "csv")));
        actions.Children.Add(MakeAction("JSON", () => _controller.ExportContestScores(contest.Id, "json")));
        actions.Children.Add(MakeAction("HTML", () => _controller.ExportContestScores(contest.Id, "html")));
        actions.Children.Add(MakeAction("Suppr.", () =>
        {
            if (_controller.DeleteContest(contest.Id))
                RefreshContestList();
        }));

        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private Button MakeAction(string label, Action onClick, bool highlight = false)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 4),
            FontSize = 11,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = highlight ? SelectedBg : DefaultBg,
            BorderBrush = highlight ? AccentBorder : DefaultBorder,
            Foreground = White,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static string StatusLabel(ContestStatus status) => status switch
    {
        ContestStatus.Active => "ACTIF",
        ContestStatus.Stopped => "ARRÊTÉ",
        _ => "BROUILLON",
    };

    private static Brush StatusBrush(ContestStatus status) => status switch
    {
        ContestStatus.Active => Green,
        ContestStatus.Stopped => Gray,
        _ => Amber,
    };

    private static void Highlight(Button button, bool selected)
    {
        button.Background = selected ? SelectedBg : DefaultBg;
        button.BorderBrush = selected ? AccentBorder : DefaultBorder;
    }

    private static SolidColorBrush CreateBrush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private void OnScoresClick(object sender, RoutedEventArgs e) => _controller.ShowAllScores();
    private void OnResetCurrentTrackClick(object sender, RoutedEventArgs e) => _controller.ResetCurrentTrackScores();
    private void OnResetAllClick(object sender, RoutedEventArgs e) => _controller.ResetAllScores();
    private void OnExportCsvClick(object sender, RoutedEventArgs e) => _controller.ExportScores("csv");
    private void OnExportJsonClick(object sender, RoutedEventArgs e) => _controller.ExportScores("json");
    private void OnExportHtmlClick(object sender, RoutedEventArgs e) => _controller.ExportScores("html");
    private void OnDebugClick(object sender, RoutedEventArgs e) => _controller.ShowDebugWindow();
    private void OnShowGlobalClick(object sender, RoutedEventArgs e)
    {
        _controller.SetOverlayContest(null);
        RefreshContestList();
    }

    private void OnCreateContestClick(object sender, RoutedEventArgs e)
    {
        var name = NewContestNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Indique un nom de concours.", "Concours", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var created = _controller.CreateContest(name);
        if (created is null)
            return;

        NewContestNameBox.Text = string.Empty;
        _controller.SetOverlayContest(created.Id);
        RefreshContestList();
    }

    private void OnTop5Click(object sender, RoutedEventArgs e)
    {
        _controller.SetLeaderboardSize(LeaderboardSizes.Default);
        SyncDisplayButtons();
    }

    private void OnTop10Click(object sender, RoutedEventArgs e)
    {
        _controller.SetLeaderboardSize(LeaderboardSizes.Extended);
        SyncDisplayButtons();
    }

    private void OnSizeSmallClick(object sender, RoutedEventArgs e)
    {
        _controller.SetOverlayWidth(OverlaySizes.Small);
        SyncDisplayButtons();
    }

    private void OnSizeMediumClick(object sender, RoutedEventArgs e)
    {
        _controller.SetOverlayWidth(OverlaySizes.Medium);
        SyncDisplayButtons();
    }

    private void OnSizeLargeClick(object sender, RoutedEventArgs e)
    {
        _controller.SetOverlayWidth(OverlaySizes.Large);
        SyncDisplayButtons();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
