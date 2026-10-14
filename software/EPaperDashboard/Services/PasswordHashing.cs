using EPaperDashboard.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;

namespace EPaperDashboard.Services;

internal enum PasswordCheck
{
    Failed,
    Valid,
    ValidNeedsRehash
}

/// <summary>
/// Salted PBKDF2 password hashes. Accounts created before this format stored an unsalted SHA-256 hex digest;
/// those still verify, and report <see cref="PasswordCheck.ValidNeedsRehash"/> so callers can upgrade them on login.
/// </summary>
internal static class PasswordHashing
{
    private static readonly PasswordHasher<User> Hasher = new();
    private static readonly User HashOwner = new();
    private static readonly string DummyHash = Hash(Guid.NewGuid().ToString("N"));

    public static string Hash(string password) => Hasher.HashPassword(HashOwner, password);

    public static PasswordCheck Verify(string storedHash, string password)
    {
        if (IsLegacy(storedHash))
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(storedHash), SHA256.HashData(Encoding.UTF8.GetBytes(password)))
                ? PasswordCheck.ValidNeedsRehash
                : PasswordCheck.Failed;
        }

        try
        {
            return Hasher.VerifyHashedPassword(HashOwner, storedHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Valid,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.ValidNeedsRehash,
                _ => PasswordCheck.Failed
            };
        }
        catch (FormatException)
        {
            return PasswordCheck.Failed;
        }
    }

    /// <summary>Spends the same time as a real check, so a missing account cannot be told apart by response time.</summary>
    public static void SimulateVerification(string password) => Verify(DummyHash, password);

    /// <summary>The pre-PBKDF2 format, kept only to build legacy accounts in tests.</summary>
    internal static string LegacySha256(string password) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    private static bool IsLegacy(string storedHash) =>
        storedHash.Length == 64 && storedHash.All(Uri.IsHexDigit);
}
