using EPaperDashboard.Services;

namespace EPaperDashboard.Models.Rendering;

public sealed record DashboardPreviewData(
    IReadOnlyDictionary<string, EntityState> EntityStates,
    IReadOnlyDictionary<string, List<TodoItem>> TodoItems,
    IReadOnlyDictionary<string, List<CalendarEvent>> CalendarEvents,
    IReadOnlyDictionary<string, List<WeatherForecast>> WeatherForecasts,
    IReadOnlyDictionary<string, List<RssFeedEntry>> RssFeedEntries,
    IReadOnlyDictionary<string, List<HistoryState>> HistoryData,
    IReadOnlyDictionary<string, string> GeneratedContent,
    IReadOnlyDictionary<string, DataSourceStatus> SourceStatuses,
    string AppVersion,
    DateTimeOffset FetchedAt)
{
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement> WidgetData { get; init; } = new Dictionary<string, System.Text.Json.JsonElement>();

    public static DashboardPreviewData FromSsrData(SsrData data, TimeProvider timeProvider) => new(
        data.EntityStates,
        data.TodoItems,
        data.CalendarEvents,
        data.WeatherForecasts.ToDictionary(
            item => $"{item.Key.EntityId}\u0000{item.Key.ForecastType}",
            item => item.Value),
        data.RssFeedEntries,
        data.HistoryData,
        data.AiContent,
        data.SourceStatuses,
        typeof(Services.Rendering.DashboardImageRenderingService).Assembly.GetName().Version?.ToString() ?? "?",
        timeProvider.GetUtcNow()) { WidgetData = data.WidgetData };
}
