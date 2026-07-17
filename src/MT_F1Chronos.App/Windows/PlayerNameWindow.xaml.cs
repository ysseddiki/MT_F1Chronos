using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MT_F1Chronos.App.Windows;

public partial class PlayerNameWindow : Window
{
    public string PlayerName => NameBox.Text;

    public PlayerNameWindow(string? currentName = null, IReadOnlyList<string>? recentNames = null)
    {
        InitializeComponent();
        NameBox.Text = currentName ?? string.Empty;

        if (recentNames is { Count: > 0 })
        {
            RecentPanel.ItemsSource = recentNames;
            RecentPanel.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnRecentNameClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string name })
        {
            NameBox.Text = name;
            NameBox.Focus();
            NameBox.SelectAll();
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            return;

        DialogResult = true;
        Close();
    }
}
