using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Providers;

/// <summary>
/// Provides RSS feed entry data for the rss-feed widget.
/// </summary>
public interface IRssFeedDataProvider
{
    Task<Result<List<RssFeedEntry>>> FetchRssFeedEntriesAsync(string dashboardId, string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all available RSS feed entities and fetches entries for each.
    /// </summary>
    Task<Result<Dictionary<string, List<RssFeedEntry>>>> FetchAllRssFeedEntriesAsync(string dashboardId, CancellationToken cancellationToken = default);
}
