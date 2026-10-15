using EPaperDashboard.Services;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class StandaloneStrategyTests
{
    private static Mock<IEnvironmentConfiguration> CreateValidConfig()
    {
        var config = new Mock<IEnvironmentConfiguration>();
        config.SetupGet(c => c.ClientUri).Returns(new Uri("https://example.com"));
        return config;
    }

    private static StandaloneStrategy CreateSut(IEnvironmentConfiguration config) =>
        new(NullLogger<StandaloneStrategy>.Instance, config);

    [Fact]
    public void ValidateConfiguration_AllRequiredValuesPresent_ReturnsSuccess()
    {
        var sut = CreateSut(CreateValidConfig().Object);

        sut.ValidateConfiguration().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateConfiguration_MissingClientUri_ReturnsFailureNamingIt()
    {
        var config = CreateValidConfig();
        config.SetupGet(c => c.ClientUri).Returns((Uri?)null);
        var sut = CreateSut(config.Object);

        var result = sut.ValidateConfiguration();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("CLIENT_URL");
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com/device?mode=pairing")]
    [InlineData("https://example.com/device#pairing")]
    public void ValidateConfiguration_UnsafeOrUnsupportedClientUri_ReturnsFailure(string clientUrl)
    {
        var config = CreateValidConfig();
        config.SetupGet(c => c.ClientUri).Returns(new Uri(clientUrl));
        var sut = CreateSut(config.Object);

        sut.ValidateConfiguration().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void GetHomeAssistantOAuthClient_UsesTheClientUrlAsAppUrlAndClientId()
    {
        var config = CreateValidConfig();
        config.SetupGet(c => c.ClientUri).Returns(new Uri("https://board.example.test/"));

        var client = CreateSut(config.Object).GetHomeAssistantOAuthClient(null);

        client.Value.Should().Be(new HomeAssistantOAuthClient("https://board.example.test", "https://board.example.test"));
    }

    [Fact]
    public void GetHomeAssistantOAuthClient_WithoutClientUrl_Fails()
    {
        var config = CreateValidConfig();
        config.SetupGet(c => c.ClientUri).Returns((Uri?)null);

        CreateSut(config.Object).GetHomeAssistantOAuthClient(null).Error.Should().Contain("CLIENT_URL");
    }

    [Fact]
    public void RunsRendererInProcess_IsFalse() =>
        CreateSut(CreateValidConfig().Object).RunsRendererInProcess.Should().BeFalse();
}
