using System.Net.Http.Headers;
using System.Text.Json;
using CSharpFunctionalExtensions;
using EPaperDashboard.Models;
using Microsoft.AspNetCore.WebUtilities;
using static EPaperDashboard.Services.Plugins.Google.GoogleOAuthService;

namespace EPaperDashboard.Services.Plugins.Google;

public sealed class GooglePlugin(GoogleOAuthService oauth, IHttpClientFactory clients, TimeProvider clock) : IOAuthDataProviderPlugin
{
    public ProviderManifest Manifest { get; } = new("google", "Google Calendar & Tasks",
        [new("clientId", "OAuth client ID"), new("clientSecret", "OAuth client secret", true), new("redirectUri", "OAuth redirect URI")], true,
        SetupInstructions: "Use your own Google Cloud OAuth client (Web application). Enable the Calendar and Tasks APIs and add your account as a test user if needed. "
        + "Use HTTPS, or localhost for development. After saving, sign in and allow Calendar and Tasks access. Changing credentials requires signing in again.");

    public IReadOnlyCollection<string> AuthorizationCredentialKeys { get; } = [RefreshTokenKey];
    public bool IsAuthorized(IReadOnlyDictionary<string, string> credentials) =>
        credentials.TryGetValue(RefreshTokenKey, out var token) && !string.IsNullOrEmpty(token);
    public Result<(string Url, string State), string> StartAuthorization(ProviderConnection connection, HttpContext httpContext) => oauth.Start(connection);
    public async Task<UnitResult<string>> CompleteAuthorizationAsync(string? state, string? browserState, string? code, CancellationToken cancellationToken)
    {
        var result = await oauth.CompleteAsync(state, browserState, code, cancellationToken);
        return result.IsFailure ? UnitResult.Failure(result.Error) : UnitResult.Success<string>();
    }

    public string? ValidateCredentials(IReadOnlyDictionary<string, string> credentials)
    {
        if (!credentials.TryGetValue("clientId", out var id) || string.IsNullOrWhiteSpace(id)
            || !credentials.TryGetValue("clientSecret", out var secret) || string.IsNullOrWhiteSpace(secret))
            return "Google OAuth client ID and secret are required.";
        if (!credentials.TryGetValue("redirectUri", out var redirect) || !Uri.TryCreate(redirect, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.EndsWith("/api/providers/google/callback", StringComparison.Ordinal))
            return "Use this installation's HTTPS /api/providers/google/callback URL (HTTP is allowed for localhost).";
        return null;
    }

    public async Task<Result<List<DataSource>, string>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken)
    {
        if (context.Connection is null) return "A Google connection is required";
        var token = await oauth.AccessTokenAsync(context.Connection, cancellationToken);
        if (token.IsFailure) return token.Error;
        var calendars = await GetItems("https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=250", token.Value, cancellationToken);
        if (calendars.IsFailure) return calendars.Error;
        var tasks = await GetItems("https://tasks.googleapis.com/tasks/v1/users/@me/lists?maxResults=100", token.Value, cancellationToken);
        if (tasks.IsFailure) return tasks.Error;
        return calendars.Value.Select(item => new DataSource
        {
            EntityId = "calendar/" + String(item, "id"), FriendlyName = String(item, "summary"),
            Domain = "calendar", Capabilities = [SourceCapabilities.Calendar]
        }).Concat(tasks.Value.Select(item => new DataSource
        {
            EntityId = "tasks/" + String(item, "id"), FriendlyName = String(item, "title"),
            Domain = "todo", Capabilities = [SourceCapabilities.Todo]
        })).ToList();
    }

    public async Task<Result<SourceData, string>> FetchAsync(ProviderContext context, SourceQuery query, CancellationToken cancellationToken)
    {
        if (context.Connection is null) return "A Google connection is required";
        var calendar = query.Capability == SourceCapabilities.Calendar && query.SourceId.StartsWith("calendar/", StringComparison.Ordinal);
        var todo = query.Capability == SourceCapabilities.Todo && query.SourceId.StartsWith("tasks/", StringComparison.Ordinal);
        if (!calendar && !todo) return "Google source does not support this widget's data capability";
        var token = await oauth.AccessTokenAsync(context.Connection, cancellationToken);
        if (token.IsFailure) return token.Error;
        var id = Uri.EscapeDataString(query.SourceId[(query.SourceId.IndexOf('/') + 1)..]);
        if (calendar)
        {
            var now = clock.GetUtcNow();
            var url = QueryHelpers.AddQueryString($"https://www.googleapis.com/calendar/v3/calendars/{id}/events", new Dictionary<string, string?>
            {
                ["singleEvents"] = "true", ["orderBy"] = "startTime", ["maxResults"] = "2500",
                ["timeMin"] = now.ToString("O"), ["timeMax"] = now.AddHours(Math.Clamp(query.Hours, 1, 720)).ToString("O")
            });
            var events = await GetItems(url, token.Value, cancellationToken);
            if (events.IsFailure) return events.Error;
            return new SourceData { CalendarEvents = events.Value.Where(e => String(e, "status") != "cancelled" && e.TryGetProperty("start", out _)).Select(ParseEvent).ToList() };
        }
        var items = await GetItems($"https://tasks.googleapis.com/tasks/v1/lists/{id}/tasks?maxResults=100&showCompleted=true&showHidden=true&showDeleted=false", token.Value, cancellationToken);
        if (items.IsFailure) return items.Error;
        return new SourceData { TodoItems = items.Value.Select(item => new TodoItem
        {
            Uid = String(item, "id"), Summary = String(item, "title"), Status = String(item, "status") == "completed" ? "completed" : "needs_action"
        }).ToList() };
    }

    private static CalendarEvent ParseEvent(JsonElement item)
    {
        var start = item.GetProperty("start");
        var end = item.TryGetProperty("end", out var endValue) ? endValue : start;
        return new()
        {
            Uid = String(item, "id"), Summary = String(item, "summary"), Description = String(item, "description"),
            Location = String(item, "location"), AllDay = start.TryGetProperty("date", out _),
            Start = Date(start), End = Date(end)
        };
    }
    private static string Date(JsonElement value) => value.TryGetProperty("dateTime", out var date) ? date.GetString() ?? "" : String(value, "date");

    private async Task<Result<List<JsonElement>, string>> GetItems(string url, string token, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        string? pageToken = null;
        var seen = new HashSet<string>();
        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pageToken is null ? url : QueryHelpers.AddQueryString(url, "pageToken", pageToken));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await clients.CreateClient("provider-google").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return "Google could not load this source. Check API access and reconnect the account if needed.";
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (body.TryGetProperty("items", out var page)) items.AddRange(page.EnumerateArray().Select(item => item.Clone()));
            pageToken = String(body, "nextPageToken");
            if (items.Count > 25000 || (!string.IsNullOrEmpty(pageToken) && !seen.Add(pageToken))) return "Google source exceeds the supported result limit.";
        } while (!string.IsNullOrEmpty(pageToken));
        return items;
    }
}
