using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Providers;

/// <summary>
/// Provides entity state data for widgets that display sensor/entity values
/// (e.g., header badges, weather current state, app-icon).
/// </summary>
public interface IEntityStateProvider
{
    Task<Result<List<EntityState>>> FetchEntityStatesAsync(string dashboardId, string[] entityIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches states for all entities relevant to dashboard widgets
    /// (sensors, binary sensors, persons, etc.). Excludes internal/automation domains.
    /// </summary>
    Task<Result<List<EntityState>>> FetchAllEntityStatesAsync(string dashboardId, CancellationToken cancellationToken = default);
}
