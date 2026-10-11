using CSharpFunctionalExtensions;
using EPaperDashboard.Services.Providers.HomeAssistant;
namespace EPaperDashboard.Services.Plugins.HomeAssistant;

public sealed class HomeAssistantPlugin(
    HomeAssistantService homeAssistant,
    HomeAssistantEntityStateProvider states,
    HomeAssistantCalendarDataProvider calendars,
    HomeAssistantTodoDataProvider tasks,
    HomeAssistantWeatherForecastProvider weather,
    HomeAssistantEntityHistoryProvider history,
    HomeAssistantRssFeedDataProvider feeds) : IDataProviderPlugin
{
    public ProviderManifest Manifest { get; } = new("home-assistant", "Home Assistant",
        [new("host", "Server URL"), new("token", "Long-lived access token", true)], Widgets: [new("home-assistant.weather", "weather"), new("home-assistant.forecast", "weather-forecast")]);

    public string? ValidateCredentials(IReadOnlyDictionary<string, string> credentials) =>
        !credentials.TryGetValue("host", out var host) || !Uri.TryCreate(host, UriKind.Absolute, out var uri)
        || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            ? "Enter an absolute HTTP or HTTPS Home Assistant server URL."
            : !credentials.TryGetValue("token", out var token) || string.IsNullOrWhiteSpace(token) ? "An access token is required." : null;

    private static string Scope(ProviderContext context) => context.Connection is null ? context.DashboardId
        : HomeAssistantConnectionService.ConnectionScope(context.DashboardId, context.Connection.Id);

    public async Task<Result<List<DataSource>, string>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken)
    {
        var result = await homeAssistant.FetchEntities(Scope(context), cancellationToken);
        if (result.IsFailure) return result.Error;
        return result.Value.Select(e => new DataSource
        {
            EntityId = e.EntityId, FriendlyName = e.FriendlyName, Domain = e.Domain,
            DeviceClass = e.DeviceClass, UnitOfMeasurement = e.UnitOfMeasurement, Icon = e.Icon,
            State = e.State, SupportedFeatures = e.SupportedFeatures, Capabilities = Capabilities(e)
        }).ToList();
    }

    private static string[] Capabilities(HassEntity entity)
    {
        var capabilities = new List<string> { SourceCapabilities.State, SourceCapabilities.History };
        switch (entity.Domain)
        {
            case "calendar": capabilities.Add(SourceCapabilities.Calendar); break;
            case "todo": capabilities.Add(SourceCapabilities.Todo); break;
            case "weather": capabilities.Add(SourceCapabilities.Weather); capabilities.Add(SourceCapabilities.Forecast); break;
            case "event": case "sensor": capabilities.Add(SourceCapabilities.Feed); break;
        }
        return capabilities.ToArray();
    }

    public async Task<Result<Dictionary<string, SourceData>, string>> FetchManyAsync(ProviderContext context, string capability, string[] sourceIds, int hours, CancellationToken cancellationToken)
    {
        if (capability == SourceCapabilities.State)
        {
            var result = await states.FetchEntityStatesAsync(Scope(context), sourceIds, cancellationToken);
            if (result.IsFailure) return result.Error;
            return result.Value.ToDictionary(s => s.EntityId, s => new SourceData { State = s });
        }
        if (capability == SourceCapabilities.History)
        {
            var result = await history.FetchEntityHistoryAsync(Scope(context), sourceIds, hours, cancellationToken);
            if (result.IsFailure) return result.Error;
            return result.Value.ToDictionary(s => s.Key, s => new SourceData { History = s.Value });
        }
        return "Unsupported batch capability";
    }

    public async Task<Result<SourceData, string>> FetchAsync(ProviderContext context, SourceQuery query, CancellationToken cancellationToken)
    {
        var scope = Scope(context);
        switch (query.Capability)
        {
            case SourceCapabilities.State:
                var state = await states.FetchEntityStatesAsync(scope, [query.SourceId], cancellationToken);
                return state.IsFailure ? Result.Failure<SourceData, string>(state.Error) : new SourceData { State = state.Value.FirstOrDefault() };
            case SourceCapabilities.Calendar:
                var calendar = await calendars.FetchCalendarEventsAsync(scope, query.SourceId, query.Hours, cancellationToken);
                return calendar.IsFailure ? Result.Failure<SourceData, string>(calendar.Error) : new SourceData { CalendarEvents = calendar.Value };
            case SourceCapabilities.Todo:
                var todo = await tasks.FetchTodoItemsAsync(scope, query.SourceId, cancellationToken);
                return todo.IsFailure ? Result.Failure<SourceData, string>(todo.Error) : new SourceData { TodoItems = todo.Value };
            case SourceCapabilities.Forecast:
                var forecast = await weather.FetchWeatherForecastAsync(scope, query.SourceId, query.ForecastType, cancellationToken);
                return forecast.IsFailure ? Result.Failure<SourceData, string>(forecast.Error) : new SourceData { Forecast = forecast.Value };
            case SourceCapabilities.History:
                var points = await history.FetchEntityHistoryAsync(scope, [query.SourceId], query.Hours, cancellationToken);
                return points.IsFailure ? Result.Failure<SourceData, string>(points.Error) : new SourceData { History = points.Value.GetValueOrDefault(query.SourceId) ?? [] };
            case SourceCapabilities.Feed:
                var feed = await feeds.FetchRssFeedEntriesAsync(scope, query.SourceId, cancellationToken);
                return feed.IsFailure ? Result.Failure<SourceData, string>(feed.Error) : new SourceData { Feed = feed.Value };
            default: return "Unsupported data capability";
        }
    }
}
