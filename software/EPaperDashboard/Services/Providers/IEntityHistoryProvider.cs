using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Providers;

/// <summary>
/// Provides historical entity state data for the graph widget.
/// </summary>
public interface IEntityHistoryProvider
{
    Task<Result<Dictionary<string, List<HistoryState>>>> FetchEntityHistoryAsync(string dashboardId, IEnumerable<string> entityIds, int hours = 24, CancellationToken cancellationToken = default);
}
