using System.Windows;
using System.Windows.Controls;

namespace MT_F1Chronos.App.Windows;

public partial class PasswordPromptWindow : Window
{
    public string Password => PasswordBox.Password;

    public PasswordPromptWindow(string title, string message, string? revealPassword = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;

        if (!string.IsNullOrEmpty(revealPassword))
        {
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordBox.IsEnabled = false;
            CancelButton.Visibility = Visibility.Collapsed;
            ConfirmButton.Content = "J’ai noté";

            var reveal = new TextBox
            {
                Text = revealPassword,
                IsReadOnly = true,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 16,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 14),
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF161B22")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#33FFFFFF")),
                SelectionBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFE10600")),
            };

            if (PasswordBox.Parent is Panel panel)
            {
                var index = panel.Children.IndexOf(PasswordBox);
                panel.Children.Insert(index, reveal);
            }

            Loaded += (_, _) =>
            {
                reveal.Focus();
                reveal.SelectAll();
            };
        }
        else
        {
            Loaded += (_, _) => PasswordBox.Focus();
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
