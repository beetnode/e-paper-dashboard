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
        config.SetupGet(c => c.StateSigningKey).Returns("signing-key");
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
    public void ValidateConfiguration_MissingStateSigningKey_ReturnsFailureNamingIt()
    {
        var config = CreateValidConfig();
        config.SetupGet(c => c.StateSigningKey).Returns((string?)null);
        var sut = CreateSut(config.Object);

        var result = sut.ValidateConfiguration();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("STATE_SIGNING_KEY");
    }

    [Fact]
    public void GetOAuthClientUri_ReturnsConfiguredClientUri()
    {
        var config = CreateValidConfig();
        var sut = CreateSut(config.Object);

        sut.GetOAuthClientUri().Should().Be(config.Object.ClientUri);
    }
}
