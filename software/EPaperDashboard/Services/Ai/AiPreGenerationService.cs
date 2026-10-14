using EPaperDashboard.Models;
using EPaperDashboard.Services.Providers;

namespace EPaperDashboard.Services.Ai;

public sealed class AiPreGenerationService(
    ILogger<AiPreGenerationService> logger,
    IServiceProvider serviceProvider,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AI Pre-Generation Service started");

        // Wait for the application to fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndPreGenerate(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during AI pre-generation check");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        logger.LogInformation("AI Pre-Generation Service stopped");
    }

    private async Task CheckAndPreGenerate(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dashboardService = scope.ServiceProvider.GetRequiredService<DashboardService>();
        var aiGenerationService = scope.ServiceProvider.GetRequiredService<AiDashboardGenerationService>();
        var aiContentProvider = scope.ServiceProvider.GetRequiredService<IAiContentProvider>();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();

        var allDashboards = dashboardService.GetAllDashboards();
        var now = timeProvider.GetLocalNow();

        foreach (var dashboard in allDashboards)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!HasEffectiveAiConfig(dashboard, userService))
                continue;

            // Pre-generate AI dashboard widgets (existing behavior)
            if (ShouldPreGenerateDashboard(dashboard, now))
            {
                await PreGenerateDashboardAsync(dashboard, aiGenerationService, cancellationToken);
            }

            // Pre-generate AI content widget cache
            if (ShouldPreGenerateContentWidgets(dashboard, now))
            {
                await PreGenerateContentWidgetsAsync(dashboard, aiContentProvider, cancellationToken);
            }
        }
    }

    private async Task PreGenerateDashboardAsync(
        Dashboard dashboard, AiDashboardGenerationService aiGenerationService, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Pre-generating AI dashboard for {DashboardId} ({DashboardName})",
                dashboard.Id, dashboard.Name);

            var result = await aiGenerationService.GenerateAsync(dashboard, cancellationToken: cancellationToken);

            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "Successfully pre-generated {WidgetCount} AI widgets for dashboard {DashboardId}",
                    result.Value.Widgets.Count, dashboard.Id);
            }
            else
            {
                logger.LogWarning(
                    "AI pre-generation failed for dashboard {DashboardId}: {Error}",
                    dashboard.Id, result.Error);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Exception during AI pre-generation for dashboard {DashboardId}",
                dashboard.Id);
        }
    }

    private async Task PreGenerateContentWidgetsAsync(
        Dashboard dashboard, IAiContentProvider aiContentProvider, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Pre-generating AI content widgets for dashboard {DashboardId} ({DashboardName})",
                dashboard.Id, dashboard.Name);

            await aiContentProvider.PreGenerateAllAsync(dashboard.Id.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Exception during AI content pre-generation for dashboard {DashboardId}",
                dashboard.Id);
        }
    }

    internal static bool ShouldPreGenerateDashboard(Dashboard dashboard, DateTimeOffset now)
    {
        if (!dashboard.IsAiEnabled
            || dashboard.RenderingMode != RenderingMode.Custom
            || string.IsNullOrWhiteSpace(dashboard.AiPrompt))
        {
            return false;
        }

        return IsInPreGenerationWindow(dashboard, now, dashboard.LastAiGenerationTime);
    }

    internal static bool ShouldPreGenerateContentWidgets(Dashboard dashboard, DateTimeOffset now)
    {
        if (dashboard.RenderingMode != RenderingMode.Custom)
            return false;

        // Check if the dashboard has any ai-content widgets with prompts
        var hasAiContentWidgets = dashboard.LayoutConfig?.Widgets?.Any(
            w => w.Type == "ai-content" && !string.IsNullOrWhiteSpace(
                w.Config.TryGetProperty("prompt", out var p) ? p.GetString() : null)) ?? false;

        if (!hasAiContentWidgets)
            return false;

        return IsInPreGenerationWindow(dashboard, now, dashboard.LastAiContentCacheTime);
    }

    internal static bool IsInPreGenerationWindow(Dashboard dashboard, DateTimeOffset now, DateTimeOffset? lastGenerationTime)
    {
        // Must have scheduled update times
        if (dashboard.UpdateTimes == null || dashboard.UpdateTimes.Count == 0)
        {
            return false;
        }

        var leadTimeMinutes = dashboard.AiLeadTimeMinutes > 0 ? dashboard.AiLeadTimeMinutes : 5;
        var nowLocal = now.LocalDateTime;
        var nowTimeOnly = TimeOnly.FromDateTime(nowLocal);

        // Check if any scheduled time is within the lead time window
        foreach (var updateTime in dashboard.UpdateTimes)
        {
            var minutesUntilUpdate = GetMinutesDifference(nowTimeOnly, updateTime);

            // We're in the pre-generation window: between [updateTime - leadTime] and [updateTime]
            if (minutesUntilUpdate >= 0 && minutesUntilUpdate <= leadTimeMinutes)
            {
                // Check if we already generated for *this* occurrence of the window. Anchoring to
                // today's date (not just time-of-day) matters: comparing only TimeOnly values would
                // make a generation from any previous day at a similar time-of-day look like it
                // already covered today's window, so a daily dashboard would stop regenerating for
                // good after its first successful run.
                if (lastGenerationTime.HasValue)
                {
                    var windowEnd = new DateTimeOffset(nowLocal.Date.Add(updateTime.ToTimeSpan()), now.Offset);
                    var windowStart = windowEnd.AddMinutes(-leadTimeMinutes);

                    if (lastGenerationTime.Value >= windowStart && lastGenerationTime.Value <= windowEnd)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        return false;
    }

    internal static double GetMinutesDifference(TimeOnly from, TimeOnly to)
    {
        var diff = to.ToTimeSpan() - from.ToTimeSpan();
        if (diff.TotalMinutes < -720) // Handle midnight crossing
            diff = diff.Add(TimeSpan.FromHours(24));
        return diff.TotalMinutes;
    }

    internal static bool HasEffectiveAiConfig(Dashboard dashboard, UserService userService)
    {
        if (dashboard.AiConfig != null && dashboard.AiConfig.ConnectionMode == AiConnectionMode.HomeAssistant)
        {
            return true;
        }

        var globalConfig = userService.GetAiConfig(dashboard.UserId);
        return globalConfig != null && globalConfig.ConnectionMode != AiConnectionMode.None;
    }
}
