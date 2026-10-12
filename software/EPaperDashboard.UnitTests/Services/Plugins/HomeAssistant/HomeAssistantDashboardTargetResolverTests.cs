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
    private readonly ProviderConnectionStore _connections;
    private readonly UserId _owner = UserId.New();

    public HomeAssistantDashboardTargetResolverTests()
    {
        _connections = InMemoryProviderConnectionRepository.CreateStore(new HomeAssistantAddonConnection(_deployment.Object));
    }

    private HomeAssistantDashboardTargetResolver CreateSut() => new(_environment.Object, _connections);

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
        var dashboard = new Dashboard { UserId = _owner, HomeAssistantConnectionId = connection.Id, Path = "lovelace/0" };

        var result = CreateSut().Resolve(dashboard);

        result.IsSuccess.Should().BeTrue();
        result.Value.DashboardUri.Should().Be(new Uri("https://ha.example.test/lovelace/0"));
        result.Value.Tokens.AccessToken.Should().Be("connection-token");
        result.Value.Tokens.HassUrl.Should().Be("https://ha.example.test");
    }

    [Fact]
    public void Resolve_ConnectionWithoutToken_NamesTheConnection()
    {
        var connection = new ProviderConnection { UserId = _owner, PluginId = "home-assistant", Name = "Holiday home" };
        _connections.Create(connection, new() { ["host"] = "https://ha.example.test" });
        var dashboard = new Dashboard { UserId = _owner, HomeAssistantConnectionId = connection.Id, Path = "lovelace/0" };

        CreateSut().Resolve(dashboard).Error.Should().Contain("'Holiday home'").And.Contain("not signed in");
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
    public void Resolve_AddonDefaultConnection_CannotRender()
    {
        _deployment.SetupGet(d => d.DefaultHomeAssistant).Returns(new HomeAssistantEndpoint("http://supervisor/core", "token", "/websocket"));
        var sut = CreateSut();
        var defaultConnection = sut.FindConnection(HomeAssistantAddonConnection.Id, _owner)!;
        var dashboard = new Dashboard { UserId = _owner, HomeAssistantConnectionId = defaultConnection.Id, Path = "lovelace/0" };

        sut.CanRender(defaultConnection).Should().BeFalse();
        sut.Resolve(dashboard).Error.Should().Be(HomeAssistantDashboardTargetResolver.DefaultConnectionCannotRender);
    }

    [Fact]
    public void Resolve_WithoutConnection_AsksForOne()
    {
        var dashboard = new Dashboard { UserId = _owner, Path = "lovelace/0" };

        CreateSut().Resolve(dashboard).Error.Should().Contain("Select a Home Assistant connection");
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
}
