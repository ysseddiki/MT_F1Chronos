using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using MT_F1Chronos.App.Windows;

namespace MT_F1Chronos.App.Services;

/// <summary>
/// Admin password stored as PBKDF2-SHA256 (salt + hash) under LocalAppData.
/// No embedded secret: on first use the user chooses their own password.
/// </summary>
internal static class AdminPassword
{
    private const int Iterations = 100_000;
    private const int HashLength = 32;
    private const int SaltLength = 16;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string StorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos",
            "admin.secret.json");

    public static bool IsConfigured => File.Exists(StorePath);

    /// <summary>
    /// Ensures a local password file exists. On first run, asks the user to choose one.
    /// Returns false if the user cancelled without setting a password.
    /// </summary>
    public static bool EnsureConfigured(Window? owner)
    {
        if (IsConfigured)
            return true;

        return TrySetPassword(
            owner,
            title: "Créer le mot de passe admin",
            message: "Aucun mot de passe admin n’est encore configuré.\n" +
                     "Choisis le tien (au moins 4 caractères) — il sera demandé pour ouvrir l’administration.\n\n" +
                     "Stocké uniquement sous forme de hash local.",
            allowCancel: true);
    }

    /// <summary>Opens the set/confirm dialog and replaces the stored hash.</summary>
    public static bool TrySetPassword(
        Window? owner,
        string title,
        string message,
        bool allowCancel = true)
    {
        var prompt = new SetPasswordWindow(title, message, allowCancel) { Owner = owner };
        if (prompt.ShowDialog() != true)
            return false;

        Save(prompt.Password);
        return true;
    }

    public static bool Verify(string? password)
    {
        if (string.IsNullOrEmpty(password) || !IsConfigured)
            return false;

        try
        {
            var json = File.ReadAllText(StorePath);
            var stored = JsonSerializer.Deserialize<AdminSecretFile>(json, JsonOptions);
            if (stored is null ||
                string.IsNullOrWhiteSpace(stored.Salt) ||
                string.IsNullOrWhiteSpace(stored.Hash))
                return false;

            var salt = Convert.FromBase64String(stored.Salt);
            var expected = Convert.FromBase64String(stored.Hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashLength);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static void Save(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashLength);

        var dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);

        var payload = new AdminSecretFile
        {
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash),
            Iterations = Iterations,
        };

        var tmp = StorePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload, JsonOptions));
        File.Move(tmp, StorePath, overwrite: true);
    }

    private sealed class AdminSecretFile
    {
        public string Salt { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public int Iterations { get; set; }
    }
}
