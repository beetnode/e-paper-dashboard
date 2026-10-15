using EPaperDashboard.Utilities;
using EPaperDashboard.Models.Rendering;
using Microsoft.Extensions.Caching.Memory;

namespace EPaperDashboard.Services.Providers;

/// <summary>
/// Resolves a deduplicated widget data plan into the snapshot shared by native rendering and
/// designer previews. Successful source values are cached briefly; failures are never cached.
/// </summary>
public sealed class SsrDataProvider(
    IEntityStateProvider entityStateProvider,
    ITodoDataProvider todoDataProvider,
    ICalendarDataProvider calendarDataProvider,
    IWeatherForecastProvider weatherForecastProvider,
    IEntityHistoryProvider entityHistoryProvider,
    IAiContentProvider aiContentProvider,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<SsrDataProvider> logger,
    IRssFeedDataProvider rssFeedDataProvider,
    Plugins.ProviderRegistry? plugins = null) : ISsrDataProvider
{
    private static readonly TimeSpan StateCacheDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TodoCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CalendarCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ForecastCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HistoryCacheDuration = TimeSpan.FromMinutes(1);

    public async Task<SsrData> FetchSsrDataAsync(
        string dashboardId,
        LayoutConfig layout,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
    {
        var data = new SsrData();
        var plan = WidgetDataPlan.Create(layout, plugins is null ? null : plugins.ResolveWidgetType);

        foreach (var group in plan.EntityStateIds.GroupBy(Plugins.SourceReference.ConnectionKey))
            await FetchEntityStatesAsync(dashboardId, group.OrderBy(id => id, StringComparer.Ordinal).ToArray(), data, bypassCache, cancellationToken);

        var tasks = new List<Task>();
        if (plugins is not null && entityStateProvider is Plugins.PluginDataRouter router)
        {
            foreach (var widget in layout.Widgets)
            {
                var plugin = plugins.WidgetOwner(widget.Type);
                try
                {
                    var request = plugin?.GetWidgetDataRequest(widget);
                    if (request is not null)
                        tasks.Add(FetchPluginWidgetAsync(dashboardId, widget.Id, plugin!.Manifest.Id, request, router, data, bypassCache, cancellationToken));
                }
                catch (Exception)
                {
                    SetFailure(data, $"widget:{widget.Id}", "Invalid plugin widget configuration.");
                }
            }
        }
        tasks.AddRange(plan.RssEntityIds.Select(async entityId =>
            {
                var (result, fromCache) = await FetchCachedAsync($"source:{dashboardId}:{CacheVersion(dashboardId)}:rss:{entityId}", CalendarCacheDuration,
                    () => rssFeedDataProvider.FetchRssFeedEntriesAsync(dashboardId, entityId, cancellationToken), bypassCache, cancellationToken);
                if (result.IsFailure) SetFailure(data, DataSourceKeys.Rss(entityId), result.Error);
                else
                {
                    data.RssFeedEntries[entityId] = result.Value;
                    SetSuccess(data, DataSourceKeys.Rss(entityId), result.Value.Count, fromCache);
                }
            }));
        tasks.AddRange(plan.TodoEntityIds.Select(entityId =>
            FetchTodoAsync(dashboardId, entityId, data, bypassCache, cancellationToken)));
        tasks.AddRange(plan.CalendarEntityIds.Select(entityId =>
            FetchCalendarAsync(dashboardId, entityId, data, bypassCache, cancellationToken)));
        tasks.AddRange(plan.Forecasts.Select(request =>
            FetchWeatherAsync(dashboardId, request, data, bypassCache, cancellationToken)));
        tasks.AddRange(plan.HistoryHoursByEntityId
            .GroupBy(item => (Hours: item.Value, Connection: Plugins.SourceReference.ConnectionKey(item.Key)))
            .Select(group => FetchGraphHistoryAsync(
                dashboardId,
                group.Select(item => item.Key).OrderBy(id => id, StringComparer.Ordinal).ToList(),
                group.Key.Hours,
                data,
                bypassCache,
                cancellationToken)));

        foreach (var widgetId in plan.CachedContentWidgetIds)
            ResolveCachedContent(dashboardId, widgetId, data);

        await Task.WhenAll(tasks);
        return data;
    }

    private async Task FetchPluginWidgetAsync(string dashboardId, string widgetId, string pluginId,
        Plugins.PluginWidgetRequest request, Plugins.PluginDataRouter router, SsrData data, bool bypassCache, CancellationToken ct)
    {
        var requestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request)));
        var (result, fromCache) = await FetchCachedAsync(
            $"source:{dashboardId}:{CacheVersion(dashboardId)}:widget:{pluginId}:{requestHash}",
            TimeSpan.FromSeconds(30),
            () => router.FetchAsync(dashboardId, new(request.Capability, request.Source, Parameters: request.Parameters), ct, pluginId),
            bypassCache, ct);
        if (result.IsFailure) SetFailure(data, $"widget:{widgetId}", result.Error);
        else if (result.Value.Custom is { } value)
        {
            data.WidgetData[widgetId] = value;
            SetSuccess(data, $"widget:{widgetId}", 1, fromCache);
        }
        else SetSuccess(data, $"widget:{widgetId}", 0, fromCache);
    }

    private async Task FetchEntityStatesAsync(
        string dashboardId,
        string[] entityIds,
        SsrData data,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        if (entityIds.Length == 0) return;
        var cacheKey = $"source:{dashboardId}:{CacheVersion(dashboardId)}:states:{string.Join('|', entityIds)}";
        var (result, fromCache) = await FetchCachedAsync(
            cacheKey,
            StateCacheDuration,
            () => entityStateProvider.FetchEntityStatesAsync(dashboardId, entityIds, cancellationToken),
            bypassCache,
            cancellationToken);

        if (result.IsFailure)
        {
            foreach (var entityId in entityIds)
                SetFailure(data, DataSourceKeys.Entity(entityId), result.Error);
            logger.LogWarning("Failed to fetch entity states: {Error}", result.Error);
            return;
        }

        foreach (var state in result.Value)
            data.EntityStates[state.EntityId] = state;

        foreach (var entityId in entityIds)
        {
            var count = data.EntityStates.ContainsKey(entityId) ? 1 : 0;
            SetSuccess(data, DataSourceKeys.Entity(entityId), count, fromCache);
        }
    }

    private async Task FetchTodoAsync(
        string dashboardId,
        string entityId,
        SsrData data,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        var (result, fromCache) = await FetchCachedAsync(
            $"source:{dashboardId}:{CacheVersion(dashboardId)}:todo:{entityId}",
            TodoCacheDuration,
            () => todoDataProvider.FetchTodoItemsAsync(dashboardId, entityId, cancellationToken),
            bypassCache,
            cancellationToken);

        if (result.IsSuccess)
        {
            data.TodoItems[entityId] = result.Value;
            SetSuccess(data, DataSourceKeys.Todo(entityId), result.Value.Count, fromCache);
        }
        else
        {
            SetFailure(data, DataSourceKeys.Todo(entityId), result.Error);
        }
    }

    private async Task FetchCalendarAsync(
        string dashboardId,
        string entityId,
        SsrData data,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        var (result, fromCache) = await FetchCachedAsync(
            $"source:{dashboardId}:{CacheVersion(dashboardId)}:calendar:{entityId}:168",
            CalendarCacheDuration,
            () => calendarDataProvider.FetchCalendarEventsAsync(dashboardId, entityId, 168, cancellationToken),
            bypassCache,
            cancellationToken);

        if (result.IsSuccess)
        {
            data.CalendarEvents[entityId] = result.Value;
            SetSuccess(data, DataSourceKeys.Calendar(entityId), result.Value.Count, fromCache);
        }
        else
        {
            SetFailure(data, DataSourceKeys.Calendar(entityId), result.Error);
        }
    }

    private async Task FetchWeatherAsync(
        string dashboardId,
        WeatherForecastDataKey request,
        SsrData data,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        var (result, fromCache) = await FetchCachedAsync(
            $"source:{dashboardId}:{CacheVersion(dashboardId)}:forecast:{request.EntityId}:{request.ForecastType}",
            ForecastCacheDuration,
            () => weatherForecastProvider.FetchWeatherForecastAsync(
                dashboardId,
                request.EntityId,
                request.ForecastType,
                cancellationToken),
            bypassCache,
            cancellationToken);

        if (result.IsFailure)
        {
            SetFailure(data, DataSourceKeys.Forecast(request), result.Error);
            return;
        }

        var forecast = result.Value;
        data.WeatherForecasts[request] = forecast;
        SetSuccess(data, DataSourceKeys.Forecast(request), forecast.Count, fromCache);
    }

    private async Task FetchGraphHistoryAsync(
        string dashboardId,
        List<string> entityIds,
        int hours,
        SsrData data,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        var (result, fromCache) = await FetchCachedAsync(
            $"source:{dashboardId}:{CacheVersion(dashboardId)}:history:{hours}:{string.Join('|', entityIds)}",
            HistoryCacheDuration,
            () => entityHistoryProvider.FetchEntityHistoryAsync(dashboardId, entityIds, hours, cancellationToken),
            bypassCache,
            cancellationToken);

        if (result.IsFailure)
        {
            foreach (var entityId in entityIds)
                SetFailure(data, DataSourceKeys.History(entityId), result.Error);
            return;
        }

        foreach (var entityId in entityIds)
        {
            var states = result.Value.GetValueOrDefault(entityId) ?? [];
            data.HistoryData[entityId] = states;
            SetSuccess(data, DataSourceKeys.History(entityId), states.Count, fromCache);
        }
    }

    private void ResolveCachedContent(string dashboardId, string widgetId, SsrData data)
    {
        var content = aiContentProvider.GetCachedContent(dashboardId, widgetId);
        if (!string.IsNullOrEmpty(content)) data.AiContent[widgetId] = content;
        SetSuccess(data, DataSourceKeys.Generated(widgetId), string.IsNullOrEmpty(content) ? 0 : 1, false);
    }

    private async Task<(Result<T> Result, bool FromCache)> FetchCachedAsync<T>(
        string cacheKey,
        TimeSpan duration,
        Func<Task<Result<T>>> fetch,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!bypassCache && cache.TryGetValue<T>(cacheKey, out var cached) && cached is not null)
            return (Result.Success<T>(cached), true);

        var result = await fetch().WaitAsync(cancellationToken);
        if (result.IsSuccess) cache.Set(cacheKey, result.Value, duration);
        return (result, false);
    }

    private void SetSuccess(SsrData data, string key, int itemCount, bool fromCache) =>
        data.SourceStatuses[key] = DataSourceStatus.Success(itemCount, timeProvider.GetUtcNow(), fromCache);

    private void SetFailure(SsrData data, string key, string error) =>
        data.SourceStatuses[key] = DataSourceStatus.Failed(error, timeProvider.GetUtcNow());

    private string CacheVersion(string dashboardId) => entityStateProvider is Plugins.PluginDataRouter router ? router.CacheVersion(dashboardId) : "";

}
