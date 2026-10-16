using System.Security.Cryptography;
using EPaperDashboard.Services.Firmware;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Firmware;

public sealed class FirmwareUpdateServiceTests : IDisposable
{
    private const string DownloadUrl = "https://example.test/firmware.bin";
    private const string SignatureUrl = "https://example.test/firmware.bin.sig";

    private readonly string _configDir = Path.Combine(Path.GetTempPath(), $"fw-tests-{Guid.NewGuid():N}");
    private readonly Mock<IFirmwareReleaseProvider> _provider = new();

    public void Dispose()
    {
        if (Directory.Exists(_configDir))
            Directory.Delete(_configDir, recursive: true);
    }

    private FirmwareUpdateService CreateSut()
    {
        var config = new Mock<IEnvironmentConfiguration>();
        config.SetupGet(c => c.ConfigDir).Returns(_configDir);
        config.SetupGet(c => c.FirmwareCheckInterval).Returns(TimeSpan.FromHours(6));

        return new FirmwareUpdateService(_provider.Object, config.Object, NullLogger<FirmwareUpdateService>.Instance);
    }

    private void SetupRelease(string version, string? downloadUrl = DownloadUrl, string? signatureUrl = null) =>
        _provider.Setup(p => p.GetLatestReleaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirmwareReleaseInfo(version, null, null, downloadUrl, null, signatureUrl));

    private void SetupDownload(byte[]? binary) =>
        _provider.Setup(p => p.DownloadFirmwareBinaryAsync(DownloadUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binary);

    private void SetupSignatureDownload(byte[]? signature) =>
        _provider.Setup(p => p.DownloadFirmwareBinaryAsync(SignatureUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(signature);

    [Fact]
    public void IsOtaReady_BeforeAnyCheck_IsFalse()
    {
        var sut = CreateSut();

        sut.IsOtaReady.Should().BeFalse();
    }

    [Fact]
    public async Task IsOtaReady_ReleaseWithoutBinaryAsset_IsFalse()
    {
        SetupRelease("0.5.0", downloadUrl: null);
        var sut = CreateSut();

        await sut.RefreshAsync();

        sut.GetLatestRelease()!.Version.Should().Be("0.5.0");
        sut.IsOtaReady.Should().BeFalse();
        (await sut.GetFirmwareBinaryPathAsync()).Should().BeNull();
    }

    [Fact]
    public async Task IsOtaReady_BinaryDownloadedAndCached_IsTrue()
    {
        SetupRelease("0.5.0");
        SetupDownload([1, 2, 3]);
        var sut = CreateSut();

        await sut.RefreshAsync();

        sut.IsOtaReady.Should().BeTrue();
        var path = await sut.GetFirmwareBinaryPathAsync();
        File.ReadAllBytes(path!).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task IsOtaReady_BinaryDownloadFails_IsFalse()
    {
        SetupRelease("0.5.0");
        SetupDownload(null);
        var sut = CreateSut();

        await sut.RefreshAsync();

        sut.IsOtaReady.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_SameVersionAfterFailedDownload_RetriesDownload()
    {
        SetupRelease("0.5.0");
        SetupDownload(null);
        var sut = CreateSut();
        await sut.RefreshAsync();

        SetupDownload([1]);
        await sut.RefreshAsync();

        sut.IsOtaReady.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAsync_BinaryAlreadyOnDisk_ReadyWithoutDownloading()
    {
        Directory.CreateDirectory(Path.Combine(_configDir, "firmware-cache"));
        File.WriteAllBytes(Path.Combine(_configDir, "firmware-cache", "firmware-0.5.0.bin"), [9]);
        SetupRelease("0.5.0");
        var sut = CreateSut();

        await sut.RefreshAsync();

        sut.IsOtaReady.Should().BeTrue();
        _provider.Verify(p => p.DownloadFirmwareBinaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NewerReleaseWhoseDownloadFails_DoesNotServeOlderCachedBinary()
    {
        SetupRelease("0.4.1");
        SetupDownload([4]);
        var sut = CreateSut();
        await sut.RefreshAsync();

        SetupRelease("0.5.0");
        SetupDownload(null);
        await sut.RefreshAsync();

        sut.IsOtaReady.Should().BeFalse();
        (await sut.GetFirmwareBinaryPathAsync()).Should().BeNull();
    }

    [Fact]
    public void GetFirmwareMd5_NoReadyBinary_IsNull()
    {
        var sut = CreateSut();

        sut.GetFirmwareMd5().Should().BeNull();
    }

    [Fact]
    public async Task GetFirmwareMd5_MatchesCachedFileContent()
    {
        SetupRelease("0.5.0");
        SetupDownload([1, 2, 3]);
        var sut = CreateSut();
        await sut.RefreshAsync();

        var path = await sut.GetFirmwareBinaryPathAsync();

        sut.GetFirmwareMd5().Should().Be(Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(path!))))
            .And.Be("5289df737df57326fcdd22597afb1fac");
    }

    [Fact]
    public async Task GetFirmwareMd5_NewBinaryCached_ReturnsNewHash()
    {
        SetupRelease("0.4.1");
        SetupDownload([4]);
        var sut = CreateSut();
        await sut.RefreshAsync();
        var oldMd5 = sut.GetFirmwareMd5();

        SetupRelease("0.5.0");
        SetupDownload([5]);
        await sut.RefreshAsync();

        sut.GetFirmwareMd5().Should().NotBe(oldMd5)
            .And.Be(Convert.ToHexStringLower(MD5.HashData([5])));
    }

    [Fact]
    public async Task GetFirmwareSha256_MatchesCachedFileContent()
    {
        SetupRelease("0.5.0");
        SetupDownload([1, 2, 3]);
        var sut = CreateSut();
        await sut.RefreshAsync();

        sut.GetFirmwareSha256().Should().Be(Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])))
            .And.Be("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81");
    }

    [Fact]
    public void GetFirmwareSha256_NoReadyBinary_IsNull()
    {
        var sut = CreateSut();

        sut.GetFirmwareSha256().Should().BeNull();
    }

    [Fact]
    public async Task GetFirmwareSignature_ReleaseWithSignature_ReturnsBase64OfSignatureAsset()
    {
        SetupRelease("0.5.0", signatureUrl: SignatureUrl);
        SetupDownload([1, 2, 3]);
        SetupSignatureDownload([0x30, 0x44, 0x02]);
        var sut = CreateSut();

        await sut.RefreshAsync();

        sut.GetFirmwareSignature().Should().Be(Convert.ToBase64String([0x30, 0x44, 0x02]));
    }

    [Fact]
    public async Task GetFirmwareSignature_ReleaseWithoutSignature_IsNullButBinaryStaysReady()
    {
        SetupRelease("0.5.0");
        SetupDownload([1, 2, 3]);
        var sut = CreateSut();

        await sut.RefreshAsync();

        sut.IsOtaReady.Should().BeTrue();
        sut.GetFirmwareSignature().Should().BeNull();
    }

    [Fact]
    public async Task GetFirmwareSignature_OversizedSignatureAsset_IsRejected()
    {
        SetupRelease("0.5.0", signatureUrl: SignatureUrl);
        SetupDownload([1, 2, 3]);
        SetupSignatureDownload(new byte[4096]);
        var sut = CreateSut();

        await sut.RefreshAsync();

        sut.GetFirmwareSignature().Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_SignatureDownloadFailedEarlier_RetriesSignatureWithoutRedownloadingBinary()
    {
        SetupRelease("0.5.0", signatureUrl: SignatureUrl);
        SetupDownload([1, 2, 3]);
        SetupSignatureDownload(null);
        var sut = CreateSut();
        await sut.RefreshAsync();

        SetupSignatureDownload([7]);
        await sut.RefreshAsync();

        sut.GetFirmwareSignature().Should().Be(Convert.ToBase64String([7]));
        _provider.Verify(p => p.DownloadFirmwareBinaryAsync(DownloadUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NewRelease_DoesNotServePreviousReleaseSignature()
    {
        SetupRelease("0.4.1", signatureUrl: SignatureUrl);
        SetupDownload([4]);
        SetupSignatureDownload([4]);
        var sut = CreateSut();
        await sut.RefreshAsync();

        SetupRelease("0.5.0");
        SetupDownload([5]);
        await sut.RefreshAsync();

        sut.GetFirmwareSignature().Should().BeNull();
        Directory.GetFiles(Path.Combine(_configDir, "firmware-cache"), "*.sig").Should().BeEmpty();
    }
}
