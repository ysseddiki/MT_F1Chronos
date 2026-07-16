using System.Windows;

namespace MT_F1Chronos.App.Windows;

public partial class PasswordPromptWindow : Window
{
    public string Password => PasswordBox.Password;

    public PasswordPromptWindow(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        Loaded += (_, _) => PasswordBox.Focus();
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
