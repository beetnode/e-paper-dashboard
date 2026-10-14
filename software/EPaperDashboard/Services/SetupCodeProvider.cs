using System.Security.Cryptography;
using System.Text;
using EPaperDashboard.Data.Repositories;

namespace EPaperDashboard.Services;

/// <summary>
/// Protects first-run setup on an instance that is reachable before anyone has claimed it. The code is written to
/// the service log, so only someone who can read the logs can create the first administrator. It is kept in the
/// database, so every replica accepts the same code, and it stops working once setup is complete.
/// </summary>
public sealed class SetupCodeProvider(IUserRepository users, UserService userService, IDeploymentStrategy deployment)
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 8;

    /// <summary>The code to show in the log, or null when setup is not pending.</summary>
    public string? GetPendingCode()
    {
        if (!deployment.IsUserManagementEnabled || userService.IsSetupComplete())
        {
            return null;
        }

        var code = users.GetOrCreateSetupCode(RandomNumberGenerator.GetString(Alphabet, Length));
        return $"{code[..(code.Length / 2)]}-{code[(code.Length / 2)..]}";
    }

    public bool Matches(string? entered)
    {
        var expected = GetPendingCode();
        if (expected is null || string.IsNullOrWhiteSpace(entered))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Normalize(expected)), Encoding.UTF8.GetBytes(Normalize(entered)));
    }

    public void AnnounceIfPending(ILogger logger)
    {
        var code = GetPendingCode();
        if (code is not null)
        {
            logger.LogWarning("Setup required. Open the web interface and enter setup code {SetupCode} to create the administrator", code);
        }
    }

    private static string Normalize(string code) =>
        new([.. code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
}
