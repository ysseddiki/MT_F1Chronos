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

    private readonly AppController _controller;
    private readonly AppSettings _settings;

    public AdminWindow(AppController controller, AppSettings settings)
    {
        _controller = controller;
        _settings = settings;
        InitializeComponent();
        SyncDisplayButtons();
    }

    private void SyncDisplayButtons()
    {
        var isTop10 = _settings.LeaderboardSize == LeaderboardSizes.Extended;
        Highlight(Top5Button, !isTop10);
        Highlight(Top10Button, isTop10);

        Highlight(SizeSmallButton, Math.Abs(_settings.OverlayWidth - OverlaySizes.Small) < 0.5);
        Highlight(SizeMediumButton, Math.Abs(_settings.OverlayWidth - OverlaySizes.Medium) < 0.5);
        Highlight(SizeLargeButton, Math.Abs(_settings.OverlayWidth - OverlaySizes.Large) < 0.5);
    }

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
