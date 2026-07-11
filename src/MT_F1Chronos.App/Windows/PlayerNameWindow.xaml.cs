using System.Windows;

namespace MT_F1Chronos.App.Windows;

public partial class PlayerNameWindow : Window
{
    public string PlayerName => NameBox.Text;

    public PlayerNameWindow(string? currentName = null)
    {
        InitializeComponent();
        NameBox.Text = currentName ?? string.Empty;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            return;

        DialogResult = true;
        Close();
    }
}
