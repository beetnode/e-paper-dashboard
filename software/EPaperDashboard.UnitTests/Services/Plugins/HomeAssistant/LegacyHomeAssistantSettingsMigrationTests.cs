using System.Text.Json;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Plugins;
using EPaperDashboard.Services.Plugins.HomeAssistant;
using EPaperDashboard.UnitTests.TestSupport;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Plugins.HomeAssistant;

public class LegacyHomeAssistantSettingsMigrationTests
{
    private readonly Dictionary<DashboardId, Dashboard> _stored = [];
    private readonly Mock<IDashboardRepository> _dashboards = new();
    private readonly Mock<IDeploymentStrategy> _deployment = new();
    private readonly ProviderConnectionStore _connections;
    private readonly UserId _owner = UserId.New();

    public LegacyHomeAssistantSettingsMigrationTests()
    {
        _dashboards.Setup(r => r.GetAll()).Returns(() => _stored.Values.ToList());
        _dashboards.Setup(r => r.FindById(It.IsAny<DashboardId>()))
            .Returns((DashboardId id) => _stored.TryGetValue(id, out var d) ? (d) : (Dashboard?)null);
        _dashboards.Setup(r => r.Update(It.IsAny<Dashboard>())).Callback((Dashboard d) => _stored[d.Id] = d);
        _connections = InMemoryProviderConnectionRepository.CreateStore(new HomeAssistantAddonConnection(_deployment.Object));
    }

    private void UseAddon()
    {
        _deployment.SetupGet(d => d.DefaultHomeAssistant).Returns(new HomeAssistantEndpoint("http://supervisor/core", "supervisor", "/websocket"));
    }

    private void Run() => new LegacyHomeAssistantSettingsMigration(_dashboards.Object, _connections, ImmediateUnitOfWork.Instance,
        _deployment.Object, NullLogger<LegacyHomeAssistantSettingsMigration>.Instance).Run();

    private Dashboard Store(string? host, string? token, RenderingMode mode = RenderingMode.Custom, object? config = null)
    {
        var dashboard = new Dashboard
        {
            Id = DashboardId.New(), UserId = _owner, Host = host, AccessToken = token, RenderingMode = mode,
            LayoutConfig = new LayoutConfig { Widgets = [new WidgetConfig { Id = "w", Type = "header", Config = JsonSerializer.SerializeToElement(config ?? new { }) }] }
        };
        _stored[dashboard.Id] = dashboard;
        return dashboard;
    }

    private List<ProviderConnection> HomeAssistantConnections() =>
        _connections.List(_owner).Where(c => c.PluginId == "home-assistant" && !_connections.IsDefault(c)).ToList();

    [Fact]
    public void Standalone_MovesSettingsIntoAConnectionAndQualifiesEverySource()
    {
        var qualified = SourceReference.Create(Guid.NewGuid().ToString("N"), "sensor.kept");
        var dashboard = Store("http://ha.test:8123/", "token", config: new
        {
            entityId = "weather.home",
            badges = new object[] { new { entityId = "sensor.temperature", icon = "fa-sun" }, new { entityId = qualified } },
            series = new object[] { new { entityId = "sensor.power" } }
        });
        dashboard.AiConfig = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant };
        dashboard.AiGeneratedWidgets = [new WidgetConfig { Id = "ai", Type = "todo", Config = JsonSerializer.SerializeToElement(new { entityId = "todo.shopping" }) }];

        Run();

        var connection = HomeAssistantConnections().Should().ContainSingle().Subject;
        connection.Name.Should().Be("Home Assistant (ha.test:8123)");
        _connections.Credentials(connection).Should().BeEquivalentTo(new Dictionary<string, string> { ["host"] = "http://ha.test:8123", ["token"] = "token" });
        var config = dashboard.LayoutConfig!.Widgets[0].Config;
        config.GetProperty("entityId").GetString().Should().Be(SourceReference.Create(connection.Id, "weather.home"));
        config.GetProperty("badges")[0].GetProperty("entityId").GetString().Should().Be(SourceReference.Create(connection.Id, "sensor.temperature"));
        config.GetProperty("badges")[0].GetProperty("icon").GetString().Should().Be("fa-sun");
        config.GetProperty("badges")[1].GetProperty("entityId").GetString().Should().Be(qualified);
        config.GetProperty("series")[0].GetProperty("entityId").GetString().Should().Be(SourceReference.Create(connection.Id, "sensor.power"));
        dashboard.AiGeneratedWidgets[0].Config.GetProperty("entityId").GetString().Should().Be(SourceReference.Create(connection.Id, "todo.shopping"));
        dashboard.AiConfig.HomeAssistantConnectionId.Should().Be(connection.Id);
        dashboard.Host.Should().BeNull();
        dashboard.AccessToken.Should().BeNull();
    }

    [Fact]
    public void DashboardsSharingAServerShareOneConnection_AndRenderingUsesIt()
    {
        var custom = Store("http://ha.test", "token", config: new { entityId = "sensor.x" });
        var rendered = Store("http://ha.test/", "token", RenderingMode.HomeAssistant);
        Store("http://other.test", "token");

        Run();
        Run();

        HomeAssistantConnections().Should().HaveCount(2);
        rendered.HomeAssistantConnectionId.Should().NotBeNull();
        rendered.HomeAssistantConnectionId.Should().Be(SourceReference.ConnectionKey(custom.LayoutConfig!.Widgets[0].Config.GetProperty("entityId").GetString()!));
    }

    [Fact]
    public void SelectedConnectionIsKept()
    {
        var dashboard = Store("http://ha.test", "token", RenderingMode.HomeAssistant);
        dashboard.HomeAssistantConnectionId = "chosen";

        Run();

        dashboard.HomeAssistantConnectionId.Should().Be("chosen");
    }

    [Fact]
    public void IncompleteSettingsAreRemovedWithoutAConnection()
    {
        var dashboard = Store(null, "token", config: new { entityId = "sensor.x" });

        Run();

        HomeAssistantConnections().Should().BeEmpty();
        dashboard.AccessToken.Should().BeNull();
        dashboard.LayoutConfig!.Widgets[0].Config.GetProperty("entityId").GetString().Should().Be("sensor.x");
    }

    [Fact]
    public void Addon_KeepsWidgetsOnTheDefaultConnection_AndMovesOnlyTheRenderingToken()
    {
        UseAddon();
        var custom = Store(null, "unused", config: new { entityId = "sensor.x" });
        var rendered = Store(null, "long-lived", RenderingMode.HomeAssistant);

        Run();

        var connection = HomeAssistantConnections().Should().ContainSingle().Subject;
        _connections.Credentials(connection).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["host"] = LegacyHomeAssistantSettingsMigration.AddonHomeAssistantUrl, ["token"] = "long-lived"
        });
        rendered.HomeAssistantConnectionId.Should().Be(connection.Id);
        custom.LayoutConfig!.Widgets[0].Config.GetProperty("entityId").GetString().Should().Be("sensor.x");
        custom.AccessToken.Should().BeNull();
    }
}
