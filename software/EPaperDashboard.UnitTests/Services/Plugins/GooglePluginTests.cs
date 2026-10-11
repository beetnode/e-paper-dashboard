using System.Net;
using System.Text;
using System.Text.Json;
using EPaperDashboard.Data.LiteDb;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Plugins;
using EPaperDashboard.Services.Plugins.Google;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Plugins;

public sealed class GooglePluginTests : IDisposable
{
    private const string Scopes = "https://www.googleapis.com/auth/calendar.calendarlist.readonly https://www.googleapis.com/auth/calendar.events.readonly https://www.googleapis.com/auth/tasks.readonly";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "izboard-google-" + Guid.NewGuid().ToString("N"));
    private readonly LiteDbContext db;
    private readonly ProviderConnectionStore store;
    private readonly MemoryCache cache = new(new MemoryCacheOptions());
    private readonly Handler handler = new();
    private readonly GoogleOAuthService oauth;
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new(DateTimeOffset.Parse("2026-10-11T12:00:00Z"));
    private readonly GooglePlugin plugin;
    private readonly ProviderConnection connection = new() { UserId = UserId.New(), PluginId = "google", Name = "Google account" };
    private readonly Dictionary<string, string> credentials = new()
    {
        ["clientId"] = "user-client", ["clientSecret"] = "user-secret", ["redirectUri"] = "https://example.test/api/providers/google/callback"
    };

    public GooglePluginTests()
    {
        Directory.CreateDirectory(directory);
        var environment = new Mock<IEnvironmentConfiguration>(); environment.SetupGet(e => e.ConfigDir).Returns(directory);
        db = new(environment.Object);
        store = new(new LiteDbProviderConnectionRepository(db), new EphemeralDataProtectionProvider());
        store.Create(connection, credentials);
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient("provider-google")).Returns(() => new HttpClient(handler, false));
        oauth = new(store, factory.Object, cache, protection, time);
        plugin = new(oauth, factory.Object, TimeProvider.System);
    }

    private void Token(string? scope = Scopes) => handler.Replies.Enqueue(JsonSerializer.Serialize(new
    { access_token = "access", refresh_token = "refresh", expires_in = 3600, scope }));

    [Fact]
    public async Task PluginExposesSignInThroughTheGenericOAuthContract()
    {
        new ProviderRegistry([plugin]).Find("google").Should().BeAssignableTo<IOAuthDataProviderPlugin>();
        plugin.IsAuthorized(credentials).Should().BeFalse();
        var (_, state) = plugin.StartAuthorization(connection, new Microsoft.AspNetCore.Http.DefaultHttpContext()).Value;
        Token();
        (await plugin.CompleteAuthorizationAsync(state, state, "code", default)).IsSuccess.Should().BeTrue();
        var saved = store.Credentials(store.Find(connection.Id, connection.UserId)!);
        plugin.IsAuthorized(saved).Should().BeTrue();
        plugin.AuthorizationCredentialKeys.Should().BeEquivalentTo(saved.Keys.Except(credentials.Keys));
        (await plugin.CompleteAuthorizationAsync(state, state, "code", default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizationIsBrowserBoundOneTimeAndUsesTheUsersClientCredentials()
    {
        var (url, state) = oauth.Start(connection);
        var query = QueryHelpers.ParseQuery(new Uri(url).Query);
        query["client_id"].ToString().Should().Be("user-client");
        query["scope"].ToString().Should().Be(Scopes);
        query["code_challenge_method"].ToString().Should().Be("S256");
        (await oauth.CompleteAsync(state, "other-browser", "code", default)).IsFailure.Should().BeTrue();
        handler.Requests.Should().BeEmpty();
        Token();
        (await oauth.CompleteAsync(state, state, "code", default)).IsSuccess.Should().BeTrue();
        var form = QueryHelpers.ParseQuery(handler.Requests.Single().Body!);
        form["client_secret"].ToString().Should().Be("user-secret");
        form["code_verifier"].ToString().Should().NotBeNullOrEmpty();
        store.Credentials(store.Find(connection.Id, connection.UserId)!)["refreshToken"].Should().Be("refresh");
        (await oauth.CompleteAsync(state, state, "code", default)).IsFailure.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AuthorizationStateSurvivesARestartButExpires()
    {
        var (_, state) = oauth.Start(connection);
        var restarted = new GoogleOAuthService(store, new Mock<IHttpClientFactory>().Object, new MemoryCache(new MemoryCacheOptions()), protection, time);
        time.Advance(TimeSpan.FromMinutes(11));
        (await restarted.CompleteAsync(state, state, "code", default)).IsFailure.Should().BeTrue();

        var (_, fresh) = oauth.Start(connection);
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient("provider-google")).Returns(() => new HttpClient(handler, false));
        Token();
        (await new GoogleOAuthService(store, factory.Object, new MemoryCache(new MemoryCacheOptions()), protection, time)
            .CompleteAsync(fresh, fresh, "code", default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EditingOrDeletingConnectionInvalidatesPendingAuthorization()
    {
        var (_, state) = oauth.Start(connection);
        store.Update(connection, new(credentials) { ["clientSecret"] = "changed" });
        (await oauth.CompleteAsync(state, state, "code", default)).IsFailure.Should().BeTrue();
        var (_, state2) = oauth.Start(connection);
        store.Delete(connection.Id, connection.UserId);
        (await oauth.CompleteAsync(state2, state2, "code", default)).IsFailure.Should().BeTrue();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingScopesAreReportedAndDoNotStoreRefreshToken()
    {
        var (_, state) = oauth.Start(connection); Token("https://www.googleapis.com/auth/tasks.readonly");
        (await oauth.CompleteAsync(state, state, "code", default)).IsFailure.Should().BeTrue();
        store.Credentials(store.Find(connection.Id, connection.UserId)!).Should().NotContainKey("refreshToken");
    }

    private void Connect()
    {
        credentials["refreshToken"] = "refresh";
        store.Update(connection, credentials);
        Token();
    }

    [Fact]
    public async Task TasksArePaginatedAndTranslatedToTheSharedTodoContract()
    {
        Connect();
        handler.Replies.Enqueue("""{"items":[{"id":"one","title":"First","status":"needsAction"}],"nextPageToken":"next+page"}""");
        handler.Replies.Enqueue("""{"items":[{"id":"two","title":"Second","status":"completed"}]}""");
        var result = await plugin.FetchAsync(new("dashboard", connection), new("todo", "tasks/list-id"), default);
        result.IsSuccess.Should().BeTrue();
        result.Value.TodoItems.Select(t => t.Status).Should().Equal("needs_action", "completed");
        handler.Requests[2].Url.Should().Contain("pageToken=next%2Bpage");
        handler.Requests[1].Authorization.Should().Be("Bearer access");
    }

    [Fact]
    public async Task CalendarExpandsRecurringEventsAndPreservesAllDayDates()
    {
        Connect();
        handler.Replies.Enqueue("""{"items":[{"id":"a","summary":"Holiday","start":{"date":"2026-10-12"},"end":{"date":"2026-10-13"}},{"id":"b","summary":"Call","start":{"dateTime":"2026-10-12T10:00:00+02:00"},"end":{"dateTime":"2026-10-12T11:00:00+02:00"}},{"id":"c","status":"cancelled"}]}""");
        var result = await plugin.FetchAsync(new("dashboard", connection), new("calendar", "calendar/personal@example.test"), default);
        result.IsSuccess.Should().BeTrue();
        result.Value.CalendarEvents.Should().HaveCount(2);
        result.Value.CalendarEvents[0].AllDay.Should().BeTrue();
        result.Value.CalendarEvents[0].End.Should().Be("2026-10-13");
        result.Value.CalendarEvents[1].Start.Should().Be("2026-10-12T10:00:00+02:00");
        handler.Requests[1].Url.Should().Contain("singleEvents=true").And.Contain("orderBy=startTime").And.Contain("personal%40example.test");
    }

    [Fact]
    public async Task UnsupportedCapabilitiesFailBeforeMakingRequests()
    {
        var result = await plugin.FetchAsync(new("dashboard", connection), new("history", "calendar/primary"), default);
        result.IsFailure.Should().BeTrue();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GoogleErrorBodiesAreNotReturnedToTheBrowser()
    {
        handler.Status = HttpStatusCode.BadRequest;
        handler.Replies.Enqueue("user-secret private-response");
        var (_, state) = oauth.Start(connection);
        var result = await oauth.CompleteAsync(state, state, "code", default);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotContain("user-secret").And.NotContain("private-response");
    }

    public void Dispose() { db.Dispose(); cache.Dispose(); handler.Dispose(); Directory.Delete(directory, true); }
    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Queue<string> Replies { get; } = new();
        public List<(string Url, string? Body, string? Authorization)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken), request.Headers.Authorization?.ToString()));
            return new(Status) { Content = new StringContent(Replies.Dequeue(), Encoding.UTF8, "application/json") };
        }
    }
}
