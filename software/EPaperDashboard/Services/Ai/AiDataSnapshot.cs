namespace EPaperDashboard.Services.Ai;

public sealed class AiDataSnapshot
{
    public Dictionary<string, EntityState> EntityStates { get; set; } = new();
    public Dictionary<string, List<TodoItem>> TodoItems { get; set; } = new();
    public Dictionary<string, List<CalendarEvent>> CalendarEvents { get; set; } = new();
    public Dictionary<string, List<WeatherForecast>> WeatherForecasts { get; set; } = new();
    public Dictionary<string, List<RssFeedEntry>> RssFeedEntries { get; set; } = new();
}
