using CSharpFunctionalExtensions;
using System.Security.Claims;
using EPaperDashboard.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using EPaperDashboard.Services.Providers;
using Microsoft.Extensions.Caching.Memory;
using EPaperDashboard.Data.LiteDb;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Plugins;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Plugins;

public sealed class ProviderPluginTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "izboard-plugins-" + Guid.NewGuid().ToString("N"));
    private readonly LiteDbContext db;
    private readonly LiteDbProviderConnectionRepository repository;
    private readonly ProviderConnectionStore connections;
    private readonly LiteDbDashboardRepository dashboards;
    private readonly Dashboard dashboard = new() { Id = DashboardId.New(), UserId = UserId.New() };
    private readonly FakePlugin plugin = new("example");
    private readonly FakePlugin legacy = new("home-assistant");
    private readonly PluginDataRouter router;

    public ProviderPluginTests()
    {
        Directory.CreateDirectory(directory);
        var environment = new Mock<IEnvironmentConfiguration>();
        environment.SetupGet(e => e.ConfigDir).Returns(directory);
        db = new LiteDbContext(environment.Object);
        repository = new(db);
        connections = new(repository, new EphemeralDataProtectionProvider());
        dashboards = new(db);
        dashboards.Insert(dashboard);
        new LiteDbUserRepository(db).Insert(new User { Id = dashboard.UserId, Username = "owner" });
        router = new(new DashboardService(dashboards), connections, new ProviderRegistry([plugin, legacy]),
            Mock.Of<IDeploymentStrategy>(), NullLogger<PluginDataRouter>.Instance);
    }

    private ProviderConnection Add(UserId? owner = null, string name = "Personal")
    {
        var connection = new ProviderConnection { UserId = owner ?? dashboard.UserId, PluginId = "example", Name = name };
        connections.Create(connection, new() { ["secret"] = "test-private-token" });
        return connection;
    }

    [Fact]
    public void ConnectionCredentialsAreEncryptedAndUpdatesAreConditional()
    {
        var connection = Add();
        var saved = repository.Find(connection.Id, dashboard.UserId)!;
        saved.ProtectedCredentials.Should().NotContain("test-private-token");
        connections.Credentials(saved)["secret"].Should().Be("test-private-token");
        var stale = repository.Find(connection.Id, dashboard.UserId)!;
        connections.Update(saved, new() { ["secret"] = "updated" }).Should().BeTrue();
        connections.Update(stale, new() { ["secret"] = "stale" }).Should().BeFalse();
        connections.Credentials(repository.Find(connection.Id, dashboard.UserId)!)["secret"].Should().Be("updated");
        connections.Delete(connection.Id, dashboard.UserId);
        connections.Update(saved, new() { ["secret"] = "resurrected" }).Should().BeFalse();
    }

    [Fact]
    public async Task ForeignConnectionsCannotBeDiscoveredOrFetched()
    {
        var foreign = Add(UserId.New());
        var discovery = await router.DiscoverAsync(dashboard.Id.Value, default);
        discovery.Sources.Should().BeEmpty();
        var result = await router.FetchAsync(dashboard.Id.Value, new("calendar", SourceReference.Create(foreign.Id, "calendar/primary")), default);
        result.IsFailure.Should().BeTrue();
        plugin.Fetches.Should().BeEmpty();
        connections.Find(foreign.Id, dashboard.UserId).Should().BeNull();
        connections.Delete(foreign.Id, dashboard.UserId);
        connections.Find(foreign.Id, foreign.UserId).Should().NotBeNull();
    }

    [Fact]
    public async Task SameSourceIdsFromDifferentConnectionsRemainDistinct()
    {
        var a = Add(name: "A"); var b = Add(name: "B");
        var discovery = await router.DiscoverAsync(dashboard.Id.Value, default);
        discovery.Sources.Should().HaveCount(2);
        discovery.Sources.Select(s => s.EntityId).Should().OnlyHaveUniqueItems();
        foreach (var source in discovery.Sources)
        {
            var result = await router.FetchCalendarEventsAsync(dashboard.Id.Value, source.EntityId);
            result.IsSuccess.Should().BeTrue();
            result.Value.Single().Summary.Should().Be(source.ConnectionName);
        }
        plugin.Fetches.Select(f => f.Connection!.Id).Should().BeEquivalentTo([a.Id, b.Id]);
    }

    [Fact]
    public async Task LegacySourcesKeepTheirDashboardConnection()
    {
        var result = await router.FetchCalendarEventsAsync(dashboard.Id.Value, "calendar.home");
        result.IsSuccess.Should().BeTrue();
        legacy.Fetches.Should().ContainSingle().Which.Connection.Should().BeNull();
        legacy.Fetches[0].DashboardId.Should().Be(dashboard.Id.Value);
        plugin.Fetches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("source:invalid:YWJj")]
    [InlineData("source:00000000000000000000000000000000:%%%")]
    [InlineData("source:00000000000000000000000000000000:")]
    public async Task MalformedSourceReferencesFailWithoutFallingBackToHomeAssistant(string value)
    {
        var result = await router.FetchCalendarEventsAsync(dashboard.Id.Value, value);
        result.IsFailure.Should().BeTrue();
        legacy.Fetches.Should().BeEmpty();
        var states = await router.FetchEntityStatesAsync(dashboard.Id.Value, [value]);
        states.IsFailure.Should().BeTrue();
        legacy.Fetches.Should().BeEmpty();
    }

    [Fact]
    public void SourceReferencesRoundTripUnicodeAndReservedCharacters()
    {
        var id = Guid.NewGuid().ToString("N");
        var source = "calendar/name@domain.test:買い物/?&";
        var reference = SourceReference.Create(id, source);
        reference.Should().NotContain("/");
        SourceReference.TryParse(reference, out var connection, out var decoded).Should().BeTrue();
        connection.Should().Be(id); decoded.Should().Be(source);
    }

    [Fact]
    public void CacheVersionChangesWhenCredentialsChangeOrConnectionIsRemoved()
    {
        var connection = Add();
        var initial = router.CacheVersion(dashboard.Id.Value);
        connections.Update(connection, new() { ["secret"] = "replacement" });
        var updated = router.CacheVersion(dashboard.Id.Value);
        updated.Should().NotBe(initial);
        connections.Delete(connection.Id, dashboard.UserId);
        router.CacheVersion(dashboard.Id.Value).Should().NotBe(updated);
    }

    [Fact]
    public async Task BrokenPluginDoesNotHideSourcesFromOtherConnections()
    {
        Add();
        var broken = new ProviderConnection { UserId = dashboard.UserId, PluginId = "missing", Name = "Missing plugin" };
        connections.Create(broken, []);
        var discovery = await router.DiscoverAsync(dashboard.Id.Value, default);
        discovery.Sources.Should().ContainSingle();
        discovery.Errors.Should().ContainSingle().Which.Should().Contain("Missing plugin");
    }

    [Fact]
    public async Task BespokePluginWidgetUsesAuthorizedCachedDataAndInvalidatesOnDelete()
    {
        var connection = Add();
        var registry = new ProviderRegistry([plugin, legacy]);
        var sut = new SsrDataProvider(router, router, router, router, router, Mock.Of<IAiContentProvider>(),
            new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, NullLogger<SsrDataProvider>.Instance, router, registry);
        var layout = new EPaperDashboard.Models.Rendering.LayoutConfig(100, 100, 1, 1, null!,
            [new("custom", "example.custom", new(0, 0, 1, 1), JsonSerializer.SerializeToElement(new { source = SourceReference.Create(connection.Id, "private-data") }), null)],
            0, 0, 0, 0, 0, 0, 0, 0);
        var data = await sut.FetchSsrDataAsync(dashboard.Id.Value, layout);
        data.WidgetData["custom"].GetProperty("name").GetString().Should().Be("Personal");
        data.SourceStatuses["widget:custom"].State.Should().Be("ready");
        var cached = await sut.FetchSsrDataAsync(dashboard.Id.Value, layout);
        cached.SourceStatuses["widget:custom"].FromCache.Should().BeTrue();
        plugin.Fetches.Should().ContainSingle();
        connections.Delete(connection.Id, dashboard.UserId);
        var deleted = await sut.FetchSsrDataAsync(dashboard.Id.Value, layout);
        deleted.WidgetData.Should().BeEmpty();
        deleted.SourceStatuses["widget:custom"].State.Should().Be("error");
    }

    [Fact]
    public async Task PluginSpecificRequestsCannotBeRedirectedToAnotherPlugin()
    {
        var result = await router.FetchAsync(dashboard.Id.Value, new("custom", "sensor.legacy"), default, "example");
        result.IsFailure.Should().BeTrue();
        legacy.Fetches.Should().BeEmpty();
    }

    private ProviderConnectionsController Controller()
    {
        var controller = new ProviderConnectionsController(new ProviderRegistry([plugin, legacy]), connections, null!,
            new UserService(new LiteDbUserRepository(db), dashboards, repository));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, dashboard.UserId.Value)], "test"))
        } };
        return controller;
    }

    [Fact]
    public void ConnectionApiDoesNotReturnSecretsAndRetainsBlankSecretsOnEdit()
    {
        var controller = Controller();
        var response = controller.Create(new("example", "Named connection", new() { ["secret"] = "controller-secret", ["refreshToken"] = "untrusted" }));
        var serialized = JsonSerializer.Serialize(((OkObjectResult)response).Value);
        serialized.Should().NotContain("controller-secret").And.NotContain("ProtectedCredentials");
        var connection = connections.List(dashboard.UserId).Single();
        connections.Credentials(connection).Should().NotContainKey("refreshToken");
        controller.Update(connection.Id, new("example", "Renamed", new() { ["secret"] = "" })).Should().BeOfType<OkObjectResult>();
        connections.Credentials(connections.Find(connection.Id, dashboard.UserId)!)["secret"].Should().Be("controller-secret");
        JsonSerializer.Serialize(((OkObjectResult)controller.List()).Value).Should().NotContain("controller-secret");
    }

    [Fact]
    public void ConnectionApiRejectsForeignConnectionsAndDeletedAccounts()
    {
        var foreign = Add(UserId.New());
        var controller = Controller();
        controller.Update(foreign.Id, new("example", "Changed", [])).Should().BeOfType<NotFoundResult>();
        controller.Delete(foreign.Id).Should().BeOfType<NotFoundResult>();
        controller.AuthorizeGoogle(foreign.Id).Should().BeOfType<NotFoundResult>();
        new LiteDbUserRepository(db).Delete(dashboard.UserId);
        controller.Create(new("example", "No account", [])).Should().BeOfType<ForbidResult>();
        controller.List().Should().BeOfType<ForbidResult>();
    }

    public void Dispose() { db.Dispose(); Directory.Delete(directory, true); }

    private sealed class FakePlugin(string id) : IDataProviderPlugin
    {
        public ProviderManifest Manifest { get; } = new(id, id, [new("secret", "Secret", true)], Widgets: [new(id + ".custom")]);
        public PluginWidgetRequest? GetWidgetDataRequest(EPaperDashboard.Models.Rendering.WidgetConfigEntry widget) => new(widget.Config.GetProperty("source").GetString()!, "custom");
        public List<ProviderContext> Fetches { get; } = [];
        public string? ValidateCredentials(IReadOnlyDictionary<string, string> credentials) => null;
        public Task<Result<List<DataSource>, string>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<List<DataSource>, string>([new() { EntityId = "calendar/primary", FriendlyName = "Primary", Capabilities = ["calendar"] }]));
        public Task<Result<SourceData, string>> FetchAsync(ProviderContext context, SourceQuery query, CancellationToken cancellationToken)
        {
            Fetches.Add(context);
            return Task.FromResult(Result.Success<SourceData, string>(new() { CalendarEvents = [new() { Summary = context.Connection?.Name ?? "Legacy" }], Custom = JsonSerializer.SerializeToElement(new { name = context.Connection?.Name }) }));
        }
    }
}
