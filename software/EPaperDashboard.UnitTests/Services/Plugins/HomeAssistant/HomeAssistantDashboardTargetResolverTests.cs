using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Plugins;
using EPaperDashboard.Services.Plugins.HomeAssistant;
using EPaperDashboard.UnitTests.TestSupport;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Plugins.HomeAssistant;

public class HomeAssistantDashboardTargetResolverTests
{
    private readonly Mock<IDeploymentStrategy> _deployment = new();
    private readonly Mock<IEnvironmentConfiguration> _environment = new();
    private readonly ProviderConnectionStore _connections = InMemoryProviderConnectionRepository.CreateStore();
    private readonly UserId _owner = UserId.New();

    public HomeAssistantDashboardTargetResolverTests()
    {
        _deployment.SetupGet(d => d.Mode).Returns(DeploymentMode.Standalone);
    }

    private HomeAssistantDashboardTargetResolver CreateSut() => new(_deployment.Object, _environment.Object, _connections);

    private ProviderConnection AddConnection(UserId owner, string pluginId = "home-assistant", string host = "https://ha.example.test")
    {
        var connection = new ProviderConnection { UserId = owner, PluginId = pluginId, Name = "Home" };
        _connections.Create(connection, new() { ["host"] = host, ["token"] = "connection-token" });
        return connection;
    }

    [Fact]
    public void Resolve_NamedConnection_UsesConnectionServerAndToken()
    {
        var connection = AddConnection(_owner);
        var dashboard = new Dashboard { UserId = _owner, HomeAssistantConnectionId = connection.Id, Path = "lovelace/0", Host = "https://legacy.test", AccessToken = "legacy" };

        var result = CreateSut().Resolve(dashboard);

        result.IsSuccess.Should().BeTrue();
        result.Value.DashboardUri.Should().Be(new Uri("https://ha.example.test/lovelace/0"));
        result.Value.Tokens.AccessToken.Should().Be("connection-token");
        result.Value.Tokens.HassUrl.Should().Be("https://ha.example.test");
    }

    [Fact]
    public void Resolve_ConnectionOwnedByAnotherUser_Fails()
    {
        var connection = AddConnection(UserId.New());
        var dashboard = new Dashboard { UserId = _owner, HomeAssistantConnectionId = connection.Id, Path = "lovelace/0" };

        CreateSut().Resolve(dashboard).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void FindConnection_OtherPlugin_ReturnsNull()
    {
        var connection = AddConnection(_owner, pluginId: "google");

        CreateSut().FindConnection(connection.Id, _owner).Should().BeNull();
    }

    [Fact]
    public void Resolve_LegacyDashboardSettings_UsesHostAndToken()
    {
        var dashboard = new Dashboard { UserId = _owner, Host = "http://legacy.test:8123", AccessToken = "legacy", Path = "lovelace/energy" };

        var result = CreateSut().Resolve(dashboard);

        result.Value.DashboardUri.Should().Be(new Uri("http://legacy.test:8123/lovelace/energy"));
        result.Value.Tokens.AccessToken.Should().Be("legacy");
    }

    [Fact]
    public void Resolve_AddonWithoutHost_UsesInternalHomeAssistantUrl()
    {
        _deployment.SetupGet(d => d.Mode).Returns(DeploymentMode.Addon);
        var dashboard = new Dashboard { UserId = _owner, AccessToken = "long-lived", Path = "lovelace/0" };

        CreateSut().Resolve(dashboard).Value.DashboardUri.Should().Be(new Uri(Constants.HomeAssistantInternalUrl + "/lovelace/0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_MissingPath_Fails(string? path)
    {
        var connection = AddConnection(_owner);
        var dashboard = new Dashboard { UserId = _owner, HomeAssistantConnectionId = connection.Id, Path = path };

        CreateSut().Resolve(dashboard).Error.Should().Contain("path");
    }

    [Fact]
    public void Resolve_MissingToken_FailsButUriStillResolves()
    {
        var dashboard = new Dashboard { UserId = _owner, Host = "http://legacy.test", Path = "lovelace/0" };
        var sut = CreateSut();

        sut.Resolve(dashboard).Error.Should().Contain("access token");
        sut.ResolveUri(dashboard).IsSuccess.Should().BeTrue();
    }
}
