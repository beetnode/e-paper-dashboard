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
}
