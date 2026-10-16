using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EPaperDashboard.Services;

/// <summary>
/// The self-signed certificate the device port serves over TLS. Devices pin its SHA-256
/// fingerprint when they pair, so it is created once and reused for as long as the
/// config directory exists; losing it means every device has to pair again.
/// </summary>
public static class DeviceCertificateStore
{
    public const string FileName = "device-certificate.pfx";

    // Devices check the pinned fingerprint, not the validity period, so the certificate
    // only has to outlive the deployment.
    private static readonly TimeSpan Validity = TimeSpan.FromDays(365 * 50);

    public static X509Certificate2 LoadOrCreate(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, password: null);
        }

        Directory.CreateDirectory(directory);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=izBoard device API", key, HashAlgorithmName.SHA256);
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        using var created = request.CreateSelfSigned(notBefore, notBefore + Validity);
        var pfx = created.Export(X509ContentType.Pkcs12);

        // Written to a temporary file first so a crash never leaves a truncated certificate behind.
        var temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, pfx);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.Move(temporaryPath, path);

        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }

    /// <summary>Lowercase hex SHA-256 of the certificate, the value devices pin.</summary>
    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));
}
