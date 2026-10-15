using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Providers;

/// <summary>
/// Provides calendar event data for the calendar widget.
/// </summary>
public interface ICalendarDataProvider
{
    Task<Result<List<CalendarEvent>>> FetchCalendarEventsAsync(string dashboardId, string entityId, int durationHours = 168, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all available calendar entities and fetches events for each.
    /// </summary>
    Task<Result<Dictionary<string, List<CalendarEvent>>>> FetchAllCalendarEventsAsync(string dashboardId, int durationHours = 168, CancellationToken cancellationToken = default);
}
