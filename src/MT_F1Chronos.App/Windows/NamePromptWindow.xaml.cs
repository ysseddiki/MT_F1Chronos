using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MT_F1Chronos.App.Windows;

public partial class NamePromptWindow : Window
{
    public string SessionName => NameBox.Text;

    public NamePromptWindow(string defaultName, IReadOnlyList<string> recentNames, string trackName, bool isRename = false)
    {
        InitializeComponent();
        TitleText.Text = isRename ? "Renommer le chrono" : "Nouveau chrono";
        NameBox.Text = defaultName;
        TrackHintText.Text = $"Circuit : {trackName}";
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };

        foreach (var name in recentNames)
        {
            var button = new Button
            {
                Content = name,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#FF2A2A34")!),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#44FFFFFF")!),
                Cursor = Cursors.Hand,
            };
            button.Click += (_, _) => NameBox.Text = name;
            SuggestionsPanel.Children.Add(button);
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
