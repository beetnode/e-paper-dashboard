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
    private readonly FakeDashboardPlugin legacy = new("dashboard-provider");
    private readonly FakeOAuthPlugin oauthPlugin = new("signin");
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
        router = new(new DashboardService(dashboards), connections, new ProviderRegistry([plugin, legacy, oauthPlugin]),
            NullLogger<PluginDataRouter>.Instance);
    }

    private ProviderConnection Add(UserId? owner = null, string name = "Personal", string pluginId = "example")
    {
        var connection = new ProviderConnection { UserId = owner ?? dashboard.UserId, PluginId = pluginId, Name = name };
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
        var controller = new ProviderConnectionsController(new ProviderRegistry([plugin, legacy, oauthPlugin]), connections,
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
        controller.Authorize(foreign.Id).Should().BeOfType<NotFoundResult>();
        new LiteDbUserRepository(db).Delete(dashboard.UserId);
        controller.Create(new("example", "No account", [])).Should().BeOfType<ForbidResult>();
        controller.List().Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DashboardConnectionIsDiscoveredOnlyWhenThePluginReportsIt()
    {
        (await router.DiscoverAsync(dashboard.Id.Value, default)).Sources.Should().BeEmpty();
        dashboard.AccessToken = "dashboard-token";
        dashboards.Update(dashboard);
        var discovery = await router.DiscoverAsync(dashboard.Id.Value, default);
        var source = discovery.Sources.Should().ContainSingle().Subject;
        source.EntityId.Should().Be("calendar/primary");
        source.PluginId.Should().Be("dashboard-provider");
        source.ConnectionId.Should().BeNull();
        source.ConnectionName.Should().Be("Dashboard dashboard-provider");
    }

    [Fact]
    public void CacheVersionChangesWhenTheDashboardConnectionChanges()
    {
        var initial = router.CacheVersion(dashboard.Id.Value);
        dashboard.AccessToken = "rotated";
        dashboards.Update(dashboard);
        router.CacheVersion(dashboard.Id.Value).Should().NotBe(initial);
    }

    [Fact]
    public async Task BareSourceIdsFailWithoutADashboardConnectionPlugin()
    {
        var sut = new PluginDataRouter(new DashboardService(dashboards), connections, new ProviderRegistry([plugin]), NullLogger<PluginDataRouter>.Instance);
        (await sut.FetchCalendarEventsAsync(dashboard.Id.Value, "calendar.home")).IsFailure.Should().BeTrue();
        (await sut.FetchEntityStatesAsync(dashboard.Id.Value, ["sensor.home"])).IsFailure.Should().BeTrue();
        plugin.Fetches.Should().BeEmpty();
    }

    [Fact]
    public void RegistryRejectsAmbiguousDashboardConnectionsAndInconsistentOAuthManifests()
    {
        var twoDashboardPlugins = () => new ProviderRegistry([legacy, new FakeDashboardPlugin("other")]);
        twoDashboardPlugins.Should().Throw<InvalidOperationException>();
        var oauthWithoutContract = () => new ProviderRegistry([new FakePlugin("plain", requiresOAuth: true)]);
        oauthWithoutContract.Should().Throw<InvalidOperationException>();
        new ProviderRegistry([plugin, legacy]).DashboardConnectionPlugin.Should().BeSameAs(legacy);
    }

    [Fact]
    public void AuthorizeRejectsProvidersWithoutSignIn()
    {
        var connection = Add();
        Controller().Authorize(connection.Id).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void AuthorizeStartsPluginSignInAndBindsStateToTheBrowser()
    {
        var connection = Add(pluginId: "signin");
        var controller = Controller();
        var result = controller.Authorize(connection.Id).Should().BeOfType<OkObjectResult>().Subject;
        JsonSerializer.Serialize(result.Value).Should().Contain("https://signin.test/authorize");
        oauthPlugin.Started.Should().ContainSingle().Which.Id.Should().Be(connection.Id);
        controller.Response.Headers.SetCookie.ToString().Should().Contain("izboard-signin-oauth=state-1").And.Contain("httponly");
    }

    [Fact]
    public async Task CallbackCompletesSignInForTheNamedPluginOnly()
    {
        var controller = Controller();
        controller.ControllerContext.HttpContext.Request.Headers.Cookie = "izboard-signin-oauth=state-1";
        (await controller.Callback("example", "state-1", "code")).Should().BeOfType<NotFoundResult>();
        (await controller.Callback("unknown", "state-1", "code")).Should().BeOfType<NotFoundResult>();
        oauthPlugin.Completions.Should().BeEmpty();
        var redirect = (await controller.Callback("signin", "state-1", "code")).Should().BeOfType<LocalRedirectResult>().Subject;
        redirect.Url.Should().Be("~/connections?connected=signin");
        oauthPlugin.Completions.Should().ContainSingle().Which.Should().Be(("state-1", "state-1", "code"));
        oauthPlugin.Fail = true;
        var failure = (await controller.Callback("signin", "state-1", "code")).Should().BeOfType<LocalRedirectResult>().Subject;
        failure.Url.Should().StartWith("~/connections?error=");
    }

    [Fact]
    public void ChangingUserCredentialsDiscardsOnlyAuthorizationCredentials()
    {
        var connection = Add(pluginId: "signin");
        var credentials = connections.Credentials(connection);
        credentials["token"] = "granted";
        connections.Update(connection, credentials).Should().BeTrue();
        var controller = Controller();
        JsonSerializer.Serialize(((OkObjectResult)controller.List()).Value).Should().Contain("\"connected\":true");
        controller.Update(connection.Id, new("signin", "Renamed", new() { ["secret"] = "" })).Should().BeOfType<OkObjectResult>();
        connections.Credentials(connections.Find(connection.Id, dashboard.UserId)!).Should().ContainKey("token");
        controller.Update(connection.Id, new("signin", "Renamed", new() { ["secret"] = "new-secret" })).Should().BeOfType<OkObjectResult>();
        var saved = connections.Credentials(connections.Find(connection.Id, dashboard.UserId)!);
        saved.Should().NotContainKey("token");
        saved["secret"].Should().Be("new-secret");
        JsonSerializer.Serialize(((OkObjectResult)controller.List()).Value).Should().Contain("\"connected\":false");
    }

    [Fact]
    public void UpdateThatSuppliesAuthorizationCredentialsKeepsThem()
    {
        var connection = Add(pluginId: "signin");
        var controller = Controller();
        controller.Update(connection.Id, new("signin", "Renamed", new() { ["secret"] = "new-secret", ["token"] = "pasted" }))
            .Should().BeOfType<OkObjectResult>();
        var saved = connections.Credentials(connections.Find(connection.Id, dashboard.UserId)!);
        saved["token"].Should().Be("pasted");
        saved["secret"].Should().Be("new-secret");
    }

    [Fact]
    public void PluginListAdvertisesSignInSupport()
    {
        var serialized = JsonSerializer.Serialize(((OkObjectResult)Controller().Plugins()).Value);
        serialized.Should().Contain("\"Id\":\"signin\"");
        using var document = JsonDocument.Parse(serialized);
        var plugins = document.RootElement.GetProperty("data").EnumerateArray().ToDictionary(p => p.GetProperty("Id").GetString()!);
        plugins["signin"].GetProperty("SupportsSignIn").GetBoolean().Should().BeTrue();
        plugins["signin"].GetProperty("SignInCredentialKeys").EnumerateArray().Select(k => k.GetString()).Should().Equal("token");
        plugins["example"].GetProperty("SupportsSignIn").GetBoolean().Should().BeFalse();
    }

    public void Dispose() { db.Dispose(); Directory.Delete(directory, true); }

    private class FakePlugin(string id, bool requiresOAuth = false, bool tokenField = false) : IDataProviderPlugin
    {
        public ProviderManifest Manifest { get; } = new(id, id,
            tokenField ? [new("secret", "Secret", true), new("token", "Token", true)] : [new("secret", "Secret", true)], requiresOAuth, Widgets: [new(id + ".custom")]);
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

    private sealed class FakeDashboardPlugin(string id) : FakePlugin(id), IDashboardConnectionPlugin
    {
        public string DashboardConnectionName => "Dashboard " + id;
        public bool HasDashboardConnection(Dashboard dashboard) => !string.IsNullOrEmpty(dashboard.AccessToken);
        public string DashboardConnectionVersion(Dashboard dashboard) => dashboard.AccessToken ?? "";
    }

    private sealed class FakeOAuthPlugin(string id) : FakePlugin(id, requiresOAuth: true, tokenField: true), IOAuthDataProviderPlugin
    {
        public List<ProviderConnection> Started { get; } = [];
        public List<(string?, string?, string?)> Completions { get; } = [];
        public bool Fail { get; set; }
        public IReadOnlyCollection<string> AuthorizationCredentialKeys { get; } = ["token"];
        public bool IsAuthorized(IReadOnlyDictionary<string, string> credentials) => credentials.ContainsKey("token");
        public Result<(string Url, string State), string> StartAuthorization(ProviderConnection connection, HttpContext httpContext)
        {
            Started.Add(connection);
            return ("https://signin.test/authorize", "state-" + Started.Count);
        }
        public Task<UnitResult<string>> CompleteAuthorizationAsync(string? state, string? browserState, string? code, CancellationToken cancellationToken)
        {
            Completions.Add((state, browserState, code));
            return Task.FromResult(Fail ? UnitResult.Failure("Sign-in failed") : UnitResult.Success<string>());
        }
    }
}
