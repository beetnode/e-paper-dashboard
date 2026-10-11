using System.Text.Json;

namespace EPaperDashboard.Models.Rendering;

/// <summary>
/// Deduplicated data requirements derived from a layout. Keeping this separate from data
/// retrieval makes widget/source behavior explicit and testable.
/// </summary>
public sealed class WidgetDataPlan
{
    public HashSet<string> EntityStateIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TodoEntityIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> CalendarEntityIds { get; } = new(StringComparer.Ordinal);
    public HashSet<WeatherForecastDataKey> Forecasts { get; } = [];
    public HashSet<string> RssEntityIds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> HistoryHoursByEntityId { get; } = new(StringComparer.Ordinal);
    public HashSet<string> CachedContentWidgetIds { get; } = new(StringComparer.Ordinal);

    public static WidgetDataPlan Create(LayoutConfig layout, Func<string, string>? resolveWidgetType = null)
    {
        var plan = new WidgetDataPlan();

        foreach (var widget in layout.Widgets)
        {
            var entityId = GetString(widget.Config, "entityId");
            switch (resolveWidgetType?.Invoke(widget.Type) ?? widget.Type)
            {
                case "header":
                    AddHeaderBadges(widget.Config, plan.EntityStateIds);
                    break;
                case "weather" when entityId is not null:
                    plan.EntityStateIds.Add(entityId);
                    break;
                case "weather-forecast" when entityId is not null:
                    plan.EntityStateIds.Add(entityId);
                    plan.Forecasts.Add(WeatherForecastDataKey.Create(
                        entityId,
                        GetString(widget.Config, "forecastMode") ?? "daily"));
                    break;
                case "todo" when entityId is not null:
                    plan.TodoEntityIds.Add(entityId);
                    break;
                case "calendar" when entityId is not null:
                    plan.CalendarEntityIds.Add(entityId);
                    break;
                case "rss-feed" when entityId is not null:
                    plan.RssEntityIds.Add(entityId);
                    break;
                case "graph":
                    AddGraphRequests(widget.Config, plan.HistoryHoursByEntityId);
                    break;
                case "ai-content":
                    if (!string.IsNullOrWhiteSpace(GetString(widget.Config, "prompt")))
                        plan.CachedContentWidgetIds.Add(widget.Id);
                    break;
            }
        }

        return plan;
    }

    private static void AddHeaderBadges(JsonElement config, HashSet<string> entityIds)
    {
        if (!config.TryGetProperty("badges", out var badges) || badges.ValueKind != JsonValueKind.Array)
            return;

        foreach (var badge in badges.EnumerateArray())
        {
            var entityId = GetString(badge, "entityId");
            if (entityId is not null) entityIds.Add(entityId);
        }
    }

    private static void AddGraphRequests(JsonElement config, Dictionary<string, int> hoursByEntityId)
    {
        if (!config.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Array)
            return;

        var hours = (GetString(config, "period") ?? "24h") switch
        {
            "1h" => 1,
            "6h" => 6,
            "7d" => 168,
            "30d" => 720,
            _ => 24
        };

        foreach (var item in series.EnumerateArray())
        {
            var entityId = GetString(item, "entityId");
            if (entityId is null) continue;
            hoursByEntityId[entityId] = Math.Max(hours, hoursByEntityId.GetValueOrDefault(entityId));
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
