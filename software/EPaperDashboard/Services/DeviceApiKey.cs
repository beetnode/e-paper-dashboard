using System.Security.Cryptography;
using System.Text;

namespace EPaperDashboard.Services;

/// <summary>
/// Device credentials. The plaintext key only exists on the device and, briefly, in the pairing
/// session that delivers it; the server stores and looks devices up by its hash. Keys are 32 random
/// bytes, so a plain SHA-256 is enough: there is nothing to brute-force.
/// </summary>
public static class DeviceApiKey
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}
