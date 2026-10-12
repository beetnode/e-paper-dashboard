using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Ai;
using EPaperDashboard.UnitTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Ai;

public class AiServiceFactoryTests
{
    private static AiServiceFactory CreateSut(
        IHttpClientFactory? httpClientFactory = null,
        HomeAssistantConnectionService? connectionService = null,
        ILoggerFactory? loggerFactory = null)
    {
        connectionService ??= new HomeAssistantConnectionService(
            new DashboardService(Mock.Of<IDashboardRepository>()),
            Mock.Of<IDeploymentStrategy>(),
            InMemoryProviderConnectionRepository.CreateStore());

        return new AiServiceFactory(
            httpClientFactory ?? Mock.Of<IHttpClientFactory>(),
            connectionService,
            loggerFactory ?? Mock.Of<ILoggerFactory>());
    }

    [Fact]
    public void Create_ConnectionModeNone_ReturnsFailure()
    {
        var sut = CreateSut();
        var config = new AiConfig { ConnectionMode = AiConnectionMode.None };

        var result = sut.Create(config);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not configured");
    }

    [Fact]
    public void Create_DirectModeMissingEndpoint_ReturnsFailure()
    {
        var sut = CreateSut();
        var config = new AiConfig { ConnectionMode = AiConnectionMode.Direct, DirectModel = "gpt" };

        var result = sut.Create(config);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("endpoint");
    }

    [Fact]
    public void Create_DirectModeMissingModel_ReturnsFailure()
    {
        var sut = CreateSut();
        var config = new AiConfig { ConnectionMode = AiConnectionMode.Direct, DirectEndpoint = "http://localhost" };

        var result = sut.Create(config);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("model");
    }

    [Fact]
    public void Create_DirectModeWithEndpointAndModel_ReturnsSuccess()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());
        var sut = CreateSut(loggerFactory: loggerFactory.Object);
        var config = new AiConfig
        {
            ConnectionMode = AiConnectionMode.Direct,
            DirectEndpoint = "http://localhost",
            DirectModel = "gpt-4"
        };

        var result = sut.Create(config);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<DirectAiService>();
    }

    [Fact]
    public void Create_HomeAssistantModeWithoutDashboardId_ReturnsFailure()
    {
        var sut = CreateSut();
        var config = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant };

        var result = sut.Create(config, dashboardId: null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("dashboard");
    }
}
