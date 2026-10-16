using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EPaperDashboard.Services;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public sealed class DeviceCertificateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"device-tls-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void LoadOrCreate_NoCertificateYet_CreatesEcdsaCertificateWithPrivateKey()
    {
        using var certificate = DeviceCertificateStore.LoadOrCreate(_directory);

        certificate.HasPrivateKey.Should().BeTrue();
        using var key = certificate.GetECDsaPrivateKey();
        key.Should().NotBeNull();
        key!.KeySize.Should().Be(256);
        File.Exists(Path.Combine(_directory, DeviceCertificateStore.FileName)).Should().BeTrue();
    }

    [Fact]
    public void LoadOrCreate_CalledAgain_ReturnsSameCertificate()
    {
        using var first = DeviceCertificateStore.LoadOrCreate(_directory);
        using var second = DeviceCertificateStore.LoadOrCreate(_directory);

        DeviceCertificateStore.Fingerprint(second).Should().Be(DeviceCertificateStore.Fingerprint(first));
        second.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void LoadOrCreate_ValidForDecades()
    {
        using var certificate = DeviceCertificateStore.LoadOrCreate(_directory);

        certificate.NotBefore.Should().BeBefore(DateTime.Now);
        certificate.NotAfter.Should().BeAfter(DateTime.Now.AddYears(40));
    }

    [Fact]
    public void Fingerprint_IsLowercaseSha256OfDerCertificate()
    {
        using var certificate = DeviceCertificateStore.LoadOrCreate(_directory);

        DeviceCertificateStore.Fingerprint(certificate)
            .Should().Be(Convert.ToHexStringLower(SHA256.HashData(certificate.RawData)))
            .And.MatchRegex("^[0-9a-f]{64}$");
    }
}
