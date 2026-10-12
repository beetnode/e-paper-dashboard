using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Plugins;
using EPaperDashboard.Services.Plugins.HomeAssistant;
using EPaperDashboard.UnitTests.TestSupport;
using FluentAssertions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class HomeAssistantConnectionServiceTests
{
    private readonly Mock<IDashboardRepository> _dashboards = new();
    private readonly Mock<IDeploymentStrategy> _deployment = new();
    private readonly ProviderConnectionStore _connections;
    private readonly Dashboard _dashboard = new() { Id = DashboardId.New(), UserId = UserId.New() };

    public HomeAssistantConnectionServiceTests()
    {
        _dashboards.Setup(r => r.FindById(_dashboard.Id)).Returns(_dashboard);
        _connections = InMemoryProviderConnectionRepository.CreateStore(new HomeAssistantAddonConnection(_deployment.Object));
    }

    private HomeAssistantConnectionService CreateSut() => new(new DashboardService(_dashboards.Object), _deployment.Object, _connections);

    private void UseAddon() =>
        _deployment.SetupGet(d => d.DefaultHomeAssistant).Returns(new HomeAssistantEndpoint("http://supervisor/core/", "supervisor-token", "/websocket"));

    private ProviderConnection Add(UserId owner, string pluginId = "home-assistant", string? token = "connection-token")
    {
        var connection = new ProviderConnection { UserId = owner, PluginId = pluginId, Name = "Home" };
        var credentials = new Dictionary<string, string> { ["host"] = "https://ha.example.test/" };
        if (token is not null) credentials["token"] = token;
        _connections.Create(connection, credentials);
        return connection;
    }

    [Fact]
    public void NamedConnection_UsesItsServerAndTheStandardWebSocketPath()
    {
        var connection = Add(_dashboard.UserId);

        var endpoint = CreateSut().GetConnectionInfo(connection);

        endpoint.Value.Should().Be(new HomeAssistantEndpoint("https://ha.example.test", "connection-token", "/api/websocket"));
    }

    [Fact]
    public void AddonDefaultConnection_UsesTheSupervisor()
    {
        UseAddon();
        var connection = _connections.DefaultConnection(_dashboard.UserId)!;

        var endpoint = CreateSut().GetConnectionInfo(connection);

        endpoint.Value.Should().Be(new HomeAssistantEndpoint("http://supervisor/core", "supervisor-token", "/websocket"));
    }

    [Fact]
    public void ConnectionWithoutToken_AsksForSignIn()
    {
        var connection = Add(_dashboard.UserId, token: null);

        CreateSut().GetConnectionInfo(connection).Error.Should().Contain("Sign in");
    }

    [Fact]
    public void ResolveConnection_OnlyAcceptsHomeAssistantConnectionsOfTheDashboardOwner()
    {
        var sut = CreateSut();
        var own = Add(_dashboard.UserId);

        sut.ResolveConnection(_dashboard.Id.Value, own.Id).Value.Should().BeSameAs(own);
        sut.ResolveConnection(_dashboard.Id.Value, Add(UserId.New()).Id).IsFailure.Should().BeTrue();
        sut.ResolveConnection(_dashboard.Id.Value, Add(_dashboard.UserId, "google").Id).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ResolveConnection_WithoutConnection_UsesTheDefaultConnection()
    {
        CreateSut().ResolveConnection(_dashboard.Id.Value, null).Error.Should().Contain("Select a Home Assistant connection");

        UseAddon();

        CreateSut().ResolveConnection(_dashboard.Id.Value, null).Value.Id.Should().Be(HomeAssistantAddonConnection.Id);
        CreateSut().ResolveConnection(_dashboard.Id.Value, HomeAssistantAddonConnection.Id).Value.Id.Should().Be(HomeAssistantAddonConnection.Id);
    }
}
