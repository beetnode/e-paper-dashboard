using System.Net;
using System.Text;
using EPaperDashboard.Services.Firmware;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Firmware;

public class GitHubFirmwareReleaseProviderTests
{
    private static GitHubFirmwareReleaseProvider CreateSut(string assetPattern, string? releaseJson = null)
    {
        var config = new Mock<IEnvironmentConfiguration>();
        config.SetupGet(c => c.FirmwareGitHubRepo).Returns("beetnode/izBoard");
        config.SetupGet(c => c.FirmwareAssetPattern).Returns(assetPattern);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        if (releaseJson is not null)
        {
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(new StaticResponseHandler(releaseJson)));
        }

        return new GitHubFirmwareReleaseProvider(
            httpClientFactory.Object, config.Object, NullLogger<GitHubFirmwareReleaseProvider>.Instance);
    }

    private static string ReleaseJson(params string[] assetNames) =>
        $$"""
        {
          "tag_name": "v0.8.0",
          "assets": [{{string.Join(",", assetNames.Select(name =>
              $$"""{ "name": "{{name}}", "browser_download_url": "https://example.test/{{name}}", "size": 10 }"""))}}]
        }
        """;

    [Fact]
    public async Task GetLatestReleaseAsync_SignatureAssetPublished_ReturnsItsUrl()
    {
        var sut = CreateSut("*.bin", ReleaseJson("firmware-0.8.0.zip", "firmware-0.8.0.bin.sig", "firmware-0.8.0.bin"));

        var release = await sut.GetLatestReleaseAsync();

        release!.DownloadUrl.Should().Be("https://example.test/firmware-0.8.0.bin");
        release.SignatureUrl.Should().Be("https://example.test/firmware-0.8.0.bin.sig");
    }

    [Fact]
    public async Task GetLatestReleaseAsync_NoSignatureAsset_SignatureUrlIsNull()
    {
        var sut = CreateSut("*.bin", ReleaseJson("firmware-0.8.0.bin", "other.bin.sig"));

        var release = await sut.GetLatestReleaseAsync();

        release!.DownloadUrl.Should().NotBeNull();
        release.SignatureUrl.Should().BeNull();
    }

    private sealed class StaticResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
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
