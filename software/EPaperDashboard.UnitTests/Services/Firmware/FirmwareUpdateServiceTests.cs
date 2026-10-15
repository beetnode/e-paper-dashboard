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

    private void SetupRelease(string version, string? downloadUrl = DownloadUrl) =>
        _provider.Setup(p => p.GetLatestReleaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirmwareReleaseInfo(version, null, null, downloadUrl, null));

    private void SetupDownload(byte[]? binary) =>
        _provider.Setup(p => p.DownloadFirmwareBinaryAsync(DownloadUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binary);

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
}
