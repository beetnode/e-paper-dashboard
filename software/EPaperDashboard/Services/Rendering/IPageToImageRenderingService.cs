using CSharpFunctionalExtensions;
using EPaperDashboard.Models;
using EPaperDashboard.Models.Rendering;

namespace EPaperDashboard.Services.Rendering;

public interface IPageToImageRenderingService
{
	Task<Health> GetHealth(UserId owner, DashboardId dashboardId, Uri dashboardUri);
	Task<Result<IImage>> RenderDashboardAsync(UserId owner, DashboardId dashboardId, Uri dashboardUri, Size size, HassTokens hassTokens, CancellationToken cancellationToken = default);
	Task<Result<IImage>> RenderHtmlAsync(UserId owner, string html, Size size, CancellationToken cancellationToken = default);
}
