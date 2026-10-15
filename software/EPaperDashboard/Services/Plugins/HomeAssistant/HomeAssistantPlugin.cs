using EPaperDashboard.Utilities;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Providers.HomeAssistant;
namespace EPaperDashboard.Services.Plugins.HomeAssistant;

public sealed class HomeAssistantPlugin(
    HomeAssistantService homeAssistant,
    HomeAssistantEntityStateProvider states,
    HomeAssistantCalendarDataProvider calendars,
    HomeAssistantTodoDataProvider tasks,
    HomeAssistantWeatherForecastProvider weather,
    HomeAssistantEntityHistoryProvider history,
    HomeAssistantRssFeedDataProvider feeds,
    HomeAssistantAuthService auth,
    ProviderConnectionStore connections) : IOAuthDataProviderPlugin
{
    private const string TokenKey = "token";

    public ProviderManifest Manifest { get; } = new("home-assistant", "Home Assistant",
        [new("host", "Server URL"), new(TokenKey, "Long-lived access token", true)],
        SetupInstructions: "Enter a server URL reachable from izBoard, then paste a long-lived access token or sign in with Home Assistant after saving.");

    // Sign-in is optional: it creates a long-lived token, which can also be pasted.
    public IReadOnlyCollection<string> AuthorizationCredentialKeys { get; } = [TokenKey];
    public bool IsAuthorized(IReadOnlyDictionary<string, string> credentials) =>
        credentials.TryGetValue(TokenKey, out var token) && !string.IsNullOrWhiteSpace(token);

    public Result<(string Url, string State)> StartAuthorization(ProviderConnection connection, HttpContext httpContext) =>
        auth.StartConnectionAuth(connection, connections.Credentials(connection).GetValueOrDefault("host") ?? "", httpContext);

    public async Task<Result> CompleteAuthorizationAsync(string? state, string? browserState, string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state) || state != browserState)
            return Result.Failure("Home Assistant sign-in expired or the browser does not match. Start again from Connections.");
        var result = await auth.CompleteConnectionAuthAsync(code, state, cancellationToken);
        if (result.IsFailure) return Result.Failure(result.Error);
        var connection = connections.Find(result.Value.ConnectionId, result.Value.Owner);
        if (connection is null || connection.PluginId != Manifest.Id) return Result.Failure("Connection no longer exists.");
        var credentials = connections.Credentials(connection);
        credentials[TokenKey] = result.Value.Token;
        return connections.Update(connection, credentials)
            ? Result.Success()
            : Result.Failure("Connection changed. Start sign-in again.");
    }

    public string? ValidateCredentials(IReadOnlyDictionary<string, string> credentials) =>
        !credentials.TryGetValue("host", out var host) || !Uri.TryCreate(host, UriKind.Absolute, out var uri)
        || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            ? "Enter an absolute HTTP or HTTPS Home Assistant server URL."
            : null;

    public async Task<Result<List<DataSource>>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken)
    {
        var result = await homeAssistant.FetchEntities(context.Connection, cancellationToken);
        if (result.IsFailure) return result.Error;
        return result.Value.Select(e => new DataSource
        {
            EntityId = e.EntityId, FriendlyName = e.FriendlyName, Domain = e.Domain,
            DeviceClass = e.DeviceClass, UnitOfMeasurement = e.UnitOfMeasurement, Icon = e.Icon,
            State = e.State, SupportedFeatures = e.SupportedFeatures, Capabilities = Capabilities(e)
        }).ToList();
    }

    // Domains whose history graphs meaningfully even when the current state is not numeric.
    private static readonly HashSet<string> GraphableDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "sensor", "binary_sensor", "input_number", "number", "counter", "climate", "light", "cover", "fan",
        "humidifier", "water_heater", "weather", "person", "device_tracker", "sun", "zone"
    };

    internal static string[] Capabilities(HassEntity entity)
    {
        var capabilities = new List<string> { SourceCapabilities.State };
        if (GraphableDomains.Contains(entity.Domain)
            || double.TryParse(entity.State, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            capabilities.Add(SourceCapabilities.History);
        switch (entity.Domain)
        {
            case "calendar": capabilities.Add(SourceCapabilities.Calendar); break;
            case "todo": capabilities.Add(SourceCapabilities.Todo); break;
            case "weather": capabilities.Add(SourceCapabilities.Weather); capabilities.Add(SourceCapabilities.Forecast); break;
            case "event": case "sensor": capabilities.Add(SourceCapabilities.Feed); break;
        }
        return capabilities.ToArray();
    }

    public async Task<Result<Dictionary<string, SourceData>>> FetchManyAsync(ProviderContext context, string capability, string[] sourceIds, int hours, CancellationToken cancellationToken)
    {
        if (capability == SourceCapabilities.State)
        {
            var result = await states.FetchEntityStatesAsync(context.Connection, sourceIds, cancellationToken);
            if (result.IsFailure) return result.Error;
            return result.Value.ToDictionary(s => s.EntityId, s => new SourceData { State = s });
        }
        if (capability == SourceCapabilities.History)
        {
            var result = await history.FetchEntityHistoryAsync(context.Connection, sourceIds, hours, cancellationToken);
            if (result.IsFailure) return result.Error;
            return result.Value.ToDictionary(s => s.Key, s => new SourceData { History = s.Value });
        }
        return "Unsupported batch capability";
    }

    public async Task<Result<SourceData>> FetchAsync(ProviderContext context, SourceQuery query, CancellationToken cancellationToken)
    {
        var homeAssistant = context.Connection;
        switch (query.Capability)
        {
            case SourceCapabilities.State:
                var state = await states.FetchEntityStatesAsync(homeAssistant, [query.SourceId], cancellationToken);
                return state.IsFailure ? Result.Failure<SourceData>(state.Error) : new SourceData { State = state.Value.FirstOrDefault() };
            case SourceCapabilities.Calendar:
                var calendar = await calendars.FetchCalendarEventsAsync(homeAssistant, query.SourceId, query.Hours, cancellationToken);
                return calendar.IsFailure ? Result.Failure<SourceData>(calendar.Error) : new SourceData { CalendarEvents = calendar.Value };
            case SourceCapabilities.Todo:
                var todo = await tasks.FetchTodoItemsAsync(homeAssistant, query.SourceId, cancellationToken);
                return todo.IsFailure ? Result.Failure<SourceData>(todo.Error) : new SourceData { TodoItems = todo.Value };
            case SourceCapabilities.Forecast:
                var forecast = await weather.FetchWeatherForecastAsync(homeAssistant, query.SourceId, query.ForecastType, cancellationToken);
                return forecast.IsFailure ? Result.Failure<SourceData>(forecast.Error) : new SourceData { Forecast = forecast.Value };
            case SourceCapabilities.History:
                var points = await history.FetchEntityHistoryAsync(homeAssistant, [query.SourceId], query.Hours, cancellationToken);
                return points.IsFailure ? Result.Failure<SourceData>(points.Error) : new SourceData { History = points.Value.GetValueOrDefault(query.SourceId) ?? [] };
            case SourceCapabilities.Feed:
                var feed = await feeds.FetchRssFeedEntriesAsync(homeAssistant, query.SourceId, cancellationToken);
                return feed.IsFailure ? Result.Failure<SourceData>(feed.Error) : new SourceData { Feed = feed.Value };
            default: return "Unsupported data capability";
        }
    }
}
