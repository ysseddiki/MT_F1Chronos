using System.Windows;
using System.Windows.Controls;

namespace MT_F1Chronos.App.Windows;

public partial class NamePromptWindow : Window
{
    public string SessionName => NameBox.Text;

    public NamePromptWindow(string defaultName, IReadOnlyList<string> recentNames, string trackName)
    {
        InitializeComponent();
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
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF2A2A34")!),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#44FFFFFF")!),
                Cursor = System.Windows.Input.Cursors.Hand,
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
