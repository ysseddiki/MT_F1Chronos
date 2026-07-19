using System.Security.Cryptography;

namespace MT_F1Chronos.App.Services;

/// <summary>
/// Verifies the admin password against a PBKDF2-SHA256 hash (salted).
/// The plaintext password is not stored in the binary.
/// </summary>
internal static class AdminPassword
{
    private const int Iterations = 100_000;
    private const int HashLength = 32;

    // Random 16-byte salt + PBKDF2-HMAC-SHA256 of the admin password.
    private static readonly byte[] Salt = Convert.FromBase64String("PTzqdxLxfSeCH8P8QO7Ufg==");
    private static readonly byte[] Hash = Convert.FromBase64String("mV/jvi9mPPRlSOC6Ra/va153n6BW8As6HjOPaw/zvHQ=");

    public static bool Verify(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password,
            Salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashLength);

        return CryptographicOperations.FixedTimeEquals(actual, Hash);
    }
}
