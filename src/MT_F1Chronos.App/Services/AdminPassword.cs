using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using MT_F1Chronos.App.Windows;

namespace MT_F1Chronos.App.Services;

/// <summary>
/// Admin password stored as PBKDF2-SHA256 (salt + hash) under LocalAppData.
/// No embedded secret: if the file is missing, a password is generated on first use.
/// </summary>
internal static class AdminPassword
{
    private const int Iterations = 100_000;
    private const int HashLength = 32;
    private const int SaltLength = 16;
    private const int GeneratedLength = 14;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string StorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos",
            "admin.secret.json");

    public static bool IsConfigured => File.Exists(StorePath);

    /// <summary>
    /// Ensures a local password file exists. On first run, generates a password,
    /// shows it once to the user, then persists only salt+hash.
    /// </summary>
    public static void EnsureConfigured(Window? owner)
    {
        if (IsConfigured)
            return;

        var password = GeneratePassword();
        Save(password);

        var prompt = new PasswordPromptWindow(
            "Mot de passe admin généré",
            "Aucun mot de passe admin n’était configuré.\n\n" +
            "Note-le maintenant — il ne sera plus réaffiché.\n" +
            "(Stocké uniquement sous forme de hash local.)",
            revealPassword: password)
        {
            Owner = owner,
        };
        prompt.ShowDialog();
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

    private static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(GeneratedLength);
        var chars = new char[GeneratedLength];
        for (var i = 0; i < GeneratedLength; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    private sealed class AdminSecretFile
    {
        public string Salt { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public int Iterations { get; set; }
    }
}
