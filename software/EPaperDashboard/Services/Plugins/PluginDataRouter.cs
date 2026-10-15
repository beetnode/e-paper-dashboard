using EPaperDashboard.Utilities;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Providers;

namespace EPaperDashboard.Services.Plugins;

public sealed record SourceDiscovery(List<DataSource> Sources, List<string> Errors);

/// <summary>Resolves every source through its dashboard owner, including background renders.</summary>
public sealed class PluginDataRouter(
    DashboardService dashboards,
    ProviderConnectionStore connections,
    ProviderRegistry registry,
    ILogger<PluginDataRouter> logger) : IEntityStateProvider, ICalendarDataProvider, ITodoDataProvider,
    IWeatherForecastProvider, IEntityHistoryProvider, IRssFeedDataProvider
{
    private Result<Dashboard> Dashboard(string id)
    {
        if (!DashboardId.TryParse(id, out var typed)) return "Invalid dashboard ID";
        return dashboards.GetDashboardById(typed) is { } dashboard ? dashboard : "Dashboard not found";
    }

    public string CacheVersion(string dashboardId)
    {
        var dashboard = Dashboard(dashboardId);
        if (dashboard.IsFailure) return "missing";
        return dashboard.Value.UserId + ":" + connections.CacheVersion(dashboard.Value.UserId);
    }

    public async Task<SourceDiscovery> DiscoverAsync(string dashboardId, CancellationToken cancellationToken)
    {
        var dashboard = Dashboard(dashboardId);
        if (dashboard.IsFailure) return new([], [dashboard.Error]);
        var contexts = connections.List(dashboard.Value.UserId)
            .Select(c => (Plugin: registry.Find(c.PluginId), Context: new ProviderContext(dashboardId, c))).ToList();
        var sources = new List<DataSource>();
        var errors = new List<string>();
        foreach (var (plugin, context) in contexts)
        {
            if (plugin is null) { errors.Add($"{context.Connection.Name}: plugin is unavailable"); continue; }
            var result = await SafeAsync(() => plugin.DiscoverAsync(context, cancellationToken), cancellationToken);
            if (result.IsFailure) { errors.Add($"{context.Connection.Name}: {result.Error}"); continue; }
            sources.AddRange(result.Value.Select(source => source with
            {
                // Bare source IDs belong to the default connection.
                EntityId = connections.IsDefault(context.Connection)
                    ? source.EntityId : SourceReference.Create(context.Connection.Id, source.EntityId),
                PluginId = plugin.Manifest.Id, ConnectionId = context.Connection.Id,
                ConnectionName = context.Connection.Name
            }));
        }
        return new(sources, errors);
    }

    public async Task<Result<SourceData>> FetchAsync(string dashboardId, SourceQuery query, CancellationToken cancellationToken, string? requiredPluginId = null)
    {
        var dashboard = Dashboard(dashboardId);
        if (dashboard.IsFailure) return dashboard.Error;
        if (string.IsNullOrWhiteSpace(query.SourceId) || string.IsNullOrWhiteSpace(query.Capability)) return "A source and capability are required";
        var connection = connections.DefaultConnection(dashboard.Value.UserId);
        if (SourceReference.IsQualified(query.SourceId))
        {
            if (!SourceReference.TryParse(query.SourceId, out var connectionId, out var sourceId)) return "Invalid source reference";
            connection = connections.Find(connectionId, dashboard.Value.UserId);
            if (connection is null) return "Source connection is unavailable";
            query = query with { SourceId = sourceId };
        }
        if (connection is null) return "Source connection is unavailable";
        var plugin = registry.Find(connection.PluginId);
        if (plugin is null) return "Source plugin is unavailable";
        if (requiredPluginId is not null && plugin.Manifest.Id != requiredPluginId) return "This widget requires a source from its own plugin";
        return await SafeAsync(() => plugin.FetchAsync(new(dashboardId, connection), query, cancellationToken), cancellationToken);
    }

    private async Task<Result<T>> SafeAsync<T>(Func<Task<Result<T>>> operation, CancellationToken cancellationToken)
    {
        try { return await operation(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // Do not return exception bodies: provider failures can contain credentials or remote data.
            logger.LogWarning("Provider operation failed ({ExceptionType})", exception.GetType().Name);
            return Result.Failure<T>("Provider is unavailable. Check the connection settings and try again.");
        }
    }

    private async Task<Result<T>> FetchValue<T>(string dashboardId, SourceQuery query, Func<SourceData, T> select, CancellationToken ct)
    {
        var data = await FetchAsync(dashboardId, query, ct);
        return data.IsFailure ? Result.Failure<T>(data.Error) : select(data.Value);
    }

    private async Task<Result<Dictionary<string, T>>> FetchAll<T>(string dashboardId, string capability, Func<string, Task<Result<T>>> fetch, CancellationToken ct)
    {
        var discovery = await DiscoverAsync(dashboardId, ct);
        var result = new Dictionary<string, T>();
        foreach (var source in discovery.Sources.Where(s => s.Capabilities.Contains(capability)))
        {
            var value = await fetch(source.EntityId);
            if (value.IsSuccess) result[source.EntityId] = value.Value;
        }
        return result;
    }

    private async Task<Result<Dictionary<string, SourceData>>> FetchBatch(string dashboardId, string capability, IEnumerable<string> ids, int hours, CancellationToken ct)
    {
        var dashboard = Dashboard(dashboardId);
        if (dashboard.IsFailure) return dashboard.Error;
        var values = new Dictionary<string, SourceData>();
        foreach (var group in ids.Distinct().GroupBy(SourceReference.ConnectionKey))
        {
            // Bare source IDs belong to the default connection.
            var connection = group.Key == ""
                ? connections.DefaultConnection(dashboard.Value.UserId)
                : connections.Find(group.Key, dashboard.Value.UserId);
            if (connection is null) return "Source connection is unavailable";
            var plugin = registry.Find(connection.PluginId);
            if (plugin is null) return "Source plugin is unavailable";
            var mapping = new Dictionary<string, string>();
            foreach (var id in group)
            {
                if (SourceReference.IsQualified(id))
                {
                    if (!SourceReference.TryParse(id, out _, out var sourceId)) return "Invalid source reference";
                    mapping[sourceId] = id;
                }
                else mapping[id] = id;
            }
            var result = await SafeAsync(() => plugin.FetchManyAsync(new(dashboardId, connection), capability, mapping.Keys.ToArray(), hours, ct), ct);
            if (result.IsFailure) return result.Error;
            foreach (var (sourceId, source) in result.Value)
                if (mapping.TryGetValue(sourceId, out var id)) values[id] = source;
        }
        return values;
    }

    public async Task<Result<List<EntityState>>> FetchEntityStatesAsync(string dashboardId, string[] entityIds, CancellationToken cancellationToken = default)
    {
        var result = await FetchBatch(dashboardId, SourceCapabilities.State, entityIds, 168, cancellationToken);
        if (result.IsFailure) return result.Error;
        return result.Value.Where(item => item.Value.State is not null).Select(item => item.Value.State! with { EntityId = item.Key }).ToList();
    }
    public async Task<Result<List<EntityState>>> FetchAllEntityStatesAsync(string dashboardId, CancellationToken cancellationToken = default)
    {
        var sources = await DiscoverAsync(dashboardId, cancellationToken);
        return await FetchEntityStatesAsync(dashboardId, sources.Sources.Where(s => s.Capabilities.Contains(SourceCapabilities.State)).Select(s => s.EntityId).ToArray(), cancellationToken);
    }
    public Task<Result<List<CalendarEvent>>> FetchCalendarEventsAsync(string dashboardId, string entityId, int durationHours = 168, CancellationToken cancellationToken = default) =>
        FetchValue(dashboardId, new(SourceCapabilities.Calendar, entityId, durationHours), d => d.CalendarEvents, cancellationToken);
    public Task<Result<Dictionary<string, List<CalendarEvent>>>> FetchAllCalendarEventsAsync(string dashboardId, int durationHours = 168, CancellationToken cancellationToken = default) =>
        FetchAll(dashboardId, SourceCapabilities.Calendar, id => FetchCalendarEventsAsync(dashboardId, id, durationHours, cancellationToken), cancellationToken);
    public Task<Result<List<TodoItem>>> FetchTodoItemsAsync(string dashboardId, string entityId, CancellationToken cancellationToken = default) =>
        FetchValue(dashboardId, new(SourceCapabilities.Todo, entityId), d => d.TodoItems, cancellationToken);
    public Task<Result<Dictionary<string, List<TodoItem>>>> FetchAllTodoItemsAsync(string dashboardId, CancellationToken cancellationToken = default) =>
        FetchAll(dashboardId, SourceCapabilities.Todo, id => FetchTodoItemsAsync(dashboardId, id, cancellationToken), cancellationToken);
    public Task<Result<List<WeatherForecast>>> FetchWeatherForecastAsync(string dashboardId, string entityId, string forecastType = "daily", CancellationToken cancellationToken = default) =>
        FetchValue(dashboardId, new(SourceCapabilities.Forecast, entityId, ForecastType: forecastType), d => d.Forecast, cancellationToken);
    public Task<Result<Dictionary<string, List<WeatherForecast>>>> FetchAllWeatherForecastsAsync(string dashboardId, string forecastType = "daily", CancellationToken cancellationToken = default) =>
        FetchAll(dashboardId, SourceCapabilities.Forecast, id => FetchWeatherForecastAsync(dashboardId, id, forecastType, cancellationToken), cancellationToken);
    public Task<Result<List<RssFeedEntry>>> FetchRssFeedEntriesAsync(string dashboardId, string entityId, CancellationToken cancellationToken = default) =>
        FetchValue(dashboardId, new(SourceCapabilities.Feed, entityId), d => d.Feed, cancellationToken);
    public Task<Result<Dictionary<string, List<RssFeedEntry>>>> FetchAllRssFeedEntriesAsync(string dashboardId, CancellationToken cancellationToken = default) =>
        FetchAll(dashboardId, SourceCapabilities.Feed, id => FetchRssFeedEntriesAsync(dashboardId, id, cancellationToken), cancellationToken);
    public async Task<Result<Dictionary<string, List<HistoryState>>>> FetchEntityHistoryAsync(string dashboardId, IEnumerable<string> entityIds, int hours = 24, CancellationToken cancellationToken = default)
    {
        var result = await FetchBatch(dashboardId, SourceCapabilities.History, entityIds, hours, cancellationToken);
        if (result.IsFailure) return result.Error;
        return result.Value.ToDictionary(item => item.Key, item => item.Value.History);
    }
}
