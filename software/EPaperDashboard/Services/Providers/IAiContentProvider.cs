using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Providers;

public interface IAiContentProvider
{
    Task<Result<string>> GenerateContentAsync(
        string dashboardId, string prompt, CancellationToken cancellationToken = default);

    Task<Result<string>> GenerateAndCacheContentAsync(
        string dashboardId, string widgetId, string prompt, CancellationToken cancellationToken = default);

    Task PreGenerateAllAsync(string dashboardId, CancellationToken cancellationToken = default);

    string? GetCachedContent(string dashboardId, string widgetId);
}
