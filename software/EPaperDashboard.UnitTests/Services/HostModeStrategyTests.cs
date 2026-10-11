using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class HostModeStrategyTests
{
    private static Mock<IEnvironmentConfiguration> CreateValidConfig()
    {
        var config = new Mock<IEnvironmentConfiguration>();
        config.SetupGet(c => c.ClientUri).Returns(new Uri("https://example.com"));
        config.SetupGet(c => c.StateSigningKey).Returns("signing-key");
        config.SetupGet(c => c.HomeAssistantHost).Returns("http://homeassistant.local:8123");
        return config;
    }

    private static HostModeStrategy CreateSut(IEnvironmentConfiguration config) =>
        new(NullLogger<HostModeStrategy>.Instance, config);

    [Fact]
    public void ValidateConfiguration_AllRequiredValuesPresent_ReturnsSuccess()
    {
        var sut = CreateSut(CreateValidConfig().Object);

        sut.ValidateConfiguration().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void GetHomeAssistantConnection_UsesConfiguredHomeAssistantHost()
    {
        var sut = CreateSut(CreateValidConfig().Object);
        var dashboard = new Dashboard { AccessToken = "token-123" };

        var (host, token) = sut.GetHomeAssistantConnection(dashboard);

        host.Should().Be("http://homeassistant.local:8123");
        token.Should().Be("token-123");
    }

    [Fact]
    public void GetHomeAssistantConnection_NoHomeAssistantHostConfigured_FallsBackToDefaultCoreUrl()
    {
        var config = CreateValidConfig();
        config.SetupGet(c => c.HomeAssistantHost).Returns((string?)null);
        var sut = CreateSut(config.Object);
        var dashboard = new Dashboard { AccessToken = "token-123" };

        var (host, _) = sut.GetHomeAssistantConnection(dashboard);

        host.Should().Be(Constants.HomeAssistantCoreUrl);
    }
}
