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
    public void DefaultHomeAssistant_IsNotProvided()
    {
        CreateSut(CreateValidConfig().Object).DefaultHomeAssistant.Should().BeNull();
    }
}
