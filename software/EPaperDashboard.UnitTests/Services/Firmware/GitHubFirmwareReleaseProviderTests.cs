using System.Text.Json;
using EPaperDashboard.Services.Firmware;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Firmware;

public class GitHubFirmwareReleaseProviderTests
{
    private static GitHubFirmwareReleaseProvider CreateSut(string assetPattern)
    {
        var config = new Mock<IEnvironmentConfiguration>();
        config.SetupGet(c => c.FirmwareGitHubRepo).Returns("beetnode/izBoard");
        config.SetupGet(c => c.FirmwareAssetPattern).Returns(assetPattern);

        return new GitHubFirmwareReleaseProvider(
            Mock.Of<IHttpClientFactory>(), config.Object, NullLogger<GitHubFirmwareReleaseProvider>.Instance);
    }

    [Theory]
    [InlineData("firmware.bin", true)]
    [InlineData("firmware.BIN", true)]
    [InlineData("firmware.zip", false)]
    public void MatchesAssetPattern_WildcardExtensionPattern_MatchesByExtensionCaseInsensitively(string assetName, bool expected)
    {
        var sut = CreateSut("*.bin");

        sut.MatchesAssetPattern(assetName).Should().Be(expected);
    }

    [Theory]
    [InlineData("firmware-exact.bin", true)]
    [InlineData("other.bin", false)]
    public void MatchesAssetPattern_ExactNamePattern_RequiresExactCaseInsensitiveMatch(string assetName, bool expected)
    {
        var sut = CreateSut("firmware-exact.bin");

        sut.MatchesAssetPattern(assetName).Should().Be(expected);
    }

    [Fact]
    public void SelectLatestRelease_PrefersHighestFirmwareTagOverApplicationReleases()
    {
        var release = CreateSut("*.bin").SelectLatestRelease(Releases("""
            [
              { "tag_name": "v0.7.0", "assets": [] },
              { "tag_name": "firmware-v0.5.1", "assets": [{ "name": "firmware-0.5.1.bin", "browser_download_url": "https://example.invalid/0.5.1.bin", "size": 3 }] },
              { "tag_name": "firmware-v0.5.10", "assets": [{ "name": "firmware-0.5.10.bin", "browser_download_url": "https://example.invalid/0.5.10.bin", "size": 4 }] },
              { "tag_name": "rendering-v1.0.0", "assets": [{ "name": "rendering.bin", "browser_download_url": "https://example.invalid/r.bin" }] }
            ]
            """));

        release!.Version.Should().Be("0.5.10");
        release.DownloadUrl.Should().Be("https://example.invalid/0.5.10.bin");
        release.FileSize.Should().Be(4);
    }

    [Fact]
    public void SelectLatestRelease_FallsBackToLegacyApplicationReleaseWithFirmwareBinary()
    {
        var release = CreateSut("*.bin").SelectLatestRelease(Releases("""
            [
              { "tag_name": "v0.6.0", "assets": [{ "name": "stl-0.6.0.zip", "browser_download_url": "https://example.invalid/stl.zip" }] },
              { "tag_name": "v0.5.0", "assets": [{ "name": "firmware-0.5.0.bin", "browser_download_url": "https://example.invalid/0.5.0.bin" }] },
              { "tag_name": "v0.4.1", "assets": [{ "name": "firmware-0.4.1.bin", "browser_download_url": "https://example.invalid/0.4.1.bin" }] }
            ]
            """));

        release!.Version.Should().Be("0.5.0");
    }

    [Fact]
    public void SelectLatestRelease_IgnoresDraftsPrereleasesAndUnparsableTags()
    {
        var release = CreateSut("*.bin").SelectLatestRelease(Releases("""
            [
              { "tag_name": "firmware-v0.9.0", "draft": true, "assets": [{ "name": "a.bin", "browser_download_url": "https://example.invalid/a.bin" }] },
              { "tag_name": "firmware-v0.8.0", "prerelease": true, "assets": [{ "name": "b.bin", "browser_download_url": "https://example.invalid/b.bin" }] },
              { "tag_name": "firmware-vnext", "assets": [{ "name": "c.bin", "browser_download_url": "https://example.invalid/c.bin" }] },
              { "tag_name": "dev", "assets": [{ "name": "d.bin", "browser_download_url": "https://example.invalid/d.bin" }] },
              { "tag_name": "firmware-v0.5.1", "body": "Notes", "assets": [{ "name": "e.bin", "browser_download_url": "https://example.invalid/e.bin" }] }
            ]
            """));

        release!.Version.Should().Be("0.5.1");
        release.ReleaseNotes.Should().Be("Notes");
    }

    [Fact]
    public void SelectLatestRelease_WithoutFirmwareReleases_ReturnsNull()
    {
        CreateSut("*.bin").SelectLatestRelease(Releases("""[{ "tag_name": "v0.6.0", "assets": [] }]""")).Should().BeNull();
    }

    private static JsonElement Releases(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
