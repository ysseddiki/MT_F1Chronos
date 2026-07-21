using System.Windows;

namespace MT_F1Chronos.App.Windows;

public partial class SetPasswordWindow : Window
{
    public const int MinLength = 4;

    public string Password => PasswordBox.Password;

    public SetPasswordWindow(string title, string message, bool allowCancel = true)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        CancelButton.Visibility = allowCancel ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        var confirm = ConfirmBox.Password;

        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            ShowError($"Le mot de passe doit contenir au moins {MinLength} caractères.");
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            ShowError("Les deux saisies ne correspondent pas.");
            ConfirmBox.Clear();
            ConfirmBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
