using EPaperDashboard.Utilities;
using EPaperDashboard.Guards;
using EPaperDashboard.Models;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.RenderingComponent;
using EPaperDashboard.Services.Components.Playwright;

namespace EPaperDashboard.Services.Rendering;

internal sealed class PageToImageRenderingService(
    IHttpClientFactory httpClientFactory,
    IImageFactory imageFactory,
    PlaywrightComponentManager componentManager,
    PlaywrightComponentRuntime runtime,
    ILogger<PageToImageRenderingService> logger,
    RenderingAccessService access) : IPageToImageRenderingService
{
    public async Task<Health> GetHealth(UserId owner, DashboardId dashboardId, Uri dashboardUri)
    {
        try { access.RequireAccess(owner, dashboardId); }
        catch (InvalidOperationException) { return new Health(false, false); }
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        bool dashboardHealthy;
        try
        {
            using var response = await httpClient.GetAsync(dashboardUri);
            dashboardHealthy = response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Dashboard health check failed ({ErrorType})", exception.GetType().Name);
            dashboardHealthy = false;
        }
        return new Health(componentManager.GetActiveComponent() is not null && await runtime.IsAvailableAsync(), dashboardHealthy);
    }

    public Task<Result<IImage>> RenderDashboardAsync(
        UserId owner, DashboardId dashboardId, Uri dashboardUri, Size size, HassTokens hassTokens, CancellationToken cancellationToken = default) => Result.Try(async () =>
    {
        Guard.NotNull(dashboardUri);
        Guard.NotNull(hassTokens);
        return await RenderAsync(owner, dashboardId, new RenderRequest(
            "dashboard", size.Width, size.Height,
            DashboardUri: dashboardUri.AbsoluteUri, AccessToken: hassTokens.AccessToken,
            TokenType: hassTokens.TokenType, HassUrl: hassTokens.HassUrl, ClientId: hassTokens.ClientId), cancellationToken);
    }, SafeFailure);

    public Task<Result<IImage>> RenderHtmlAsync(
        UserId owner, string html, Size size, CancellationToken cancellationToken = default) => Result.Try(async () =>
    {
        Guard.NotNull(html);
        return await RenderAsync(owner, null, new RenderRequest("html", size.Width, size.Height, Html: html), cancellationToken);
    }, SafeFailure);

    private string SafeFailure(Exception exception)
    {
        if (exception is RenderingRequestException or RenderWorkerFailedException)
        {
            logger.LogWarning("Rendering failed: {Reason}", exception.Message);
            return exception.Message;
        }
        if (exception is OperationCanceledException) return "Rendering was cancelled or timed out. Try again.";
        // Exception messages can contain credential-bearing URLs or private server paths.
        logger.LogWarning("Rendering failed ({ErrorType})", exception.GetType().Name);
        return "Rendering failed. Check Rendering settings and try again.";
    }

    private Task<IImage> RenderAsync(UserId owner, DashboardId? dashboardId, RenderRequest request, CancellationToken cancellationToken) =>
        access.RunAsync(owner, dashboardId, async token =>
    {
        request.Validate();
        using var queueTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        queueTimeout.CancelAfter(TimeSpan.FromSeconds(150));
        using var lease = await runtime.AcquireRenderAsync(queueTimeout.Token);
        var component = componentManager.GetActiveComponent();
        // Installation is started only by opting in through Rendering settings, never by a render request.
        if (component is null)
            throw new RenderingRequestException(componentManager.GetStatus().State == PlaywrightComponentState.Installing
                ? "Rendering is being prepared on this server. Try again shortly."
                : "Rendering is not available on this server yet.");
        var screenshot = await runtime.RunAsync(component, request, token);
        return imageFactory.Load(screenshot, new Size(request.Width, request.Height));
    }, cancellationToken);
}
