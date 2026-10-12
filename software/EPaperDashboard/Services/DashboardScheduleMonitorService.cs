using EPaperDashboard.Models;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services;

public class DashboardScheduleMonitorService : BackgroundService
{
    private readonly ILogger<DashboardScheduleMonitorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _missedScheduleTolerance;

    public DashboardScheduleMonitorService(
        ILogger<DashboardScheduleMonitorService> logger,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _checkInterval = EnvironmentConfiguration.DashboardScheduleCheckInterval;
        _missedScheduleTolerance = EnvironmentConfiguration.DashboardMissedScheduleTolerance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Dashboard Schedule Monitor Service started");

        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckMissedSchedules(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking missed schedules");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Dashboard Schedule Monitor Service stopped");
    }

    private async Task CheckMissedSchedules(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dashboardService = scope.ServiceProvider.GetRequiredService<DashboardService>();
        var homeAssistantService = scope.ServiceProvider.GetRequiredService<HomeAssistantService>();

        var allDashboards = dashboardService.GetAllDashboards();
        var now = _timeProvider.GetUtcNow();
        var nowTimeOnly = TimeOnly.FromDateTime(now.DateTime);

        foreach (var dashboard in allDashboards)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessDashboard(dashboard, now, nowTimeOnly, homeAssistantService);
        }
    }

    private async Task ProcessDashboard(
        Dashboard dashboard,
        DateTimeOffset now,
        TimeOnly nowTimeOnly,
        HomeAssistantService homeAssistantService)
    {
        if (dashboard.UpdateTimes == null || dashboard.UpdateTimes.Count == 0)
        {
            return;
        }

        // Notifications go to the dashboard's Home Assistant connection, or the add-on's default connection.
        if (string.IsNullOrEmpty(dashboard.HomeAssistantConnectionId) && EnvironmentConfiguration.AppMode != DeploymentMode.Addon)
        {
            return;
        }

        try
        {
            await CheckDashboardSchedule(dashboard, now, nowTimeOnly, homeAssistantService);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking schedule for dashboard {DashboardName} (ID: {DashboardId})",
                dashboard.Name, dashboard.Id);
        }
    }

    internal static DateTimeOffset? CalculateExpectedUpdateTime(
        List<TimeOnly> updateTimes,
        DateTimeOffset now,
        TimeOnly nowTimeOnly)
    {
        var scheduledTimes = updateTimes.OrderBy(t => t).ToList();
        if (scheduledTimes.Count == 0)
        {
            return null;
        }

        TimeOnly referenceTime;
        DateTime referenceDate;

        var todaySchedules = scheduledTimes.Where(t => t <= nowTimeOnly).ToList();
        if (todaySchedules.Count > 0)
        {
            referenceTime = todaySchedules.Last();
            referenceDate = now.Date;
        }
        else
        {
            referenceTime = scheduledTimes.Last();
            referenceDate = now.Date.AddDays(-1);
        }

        return new DateTimeOffset(
            referenceDate.Add(referenceTime.ToTimeSpan()),
            TimeSpan.Zero);
    }

    private async Task CheckDashboardSchedule(
        Dashboard dashboard,
        DateTimeOffset now,
        TimeOnly nowTimeOnly,
        HomeAssistantService homeAssistantService)
    {
        var expectedUpdateDateTime = CalculateExpectedUpdateTime(dashboard.UpdateTimes!, now, nowTimeOnly);
        if (!expectedUpdateDateTime.HasValue)
        {
            return;
        }

        var timeSinceExpectedUpdate = now - expectedUpdateDateTime.Value;
        if (timeSinceExpectedUpdate < _missedScheduleTolerance)
        {
            return;
        }

        // Check if the last update was before the expected update time
        bool isMissed = !dashboard.LastUpdateTime.HasValue ||
                        dashboard.LastUpdateTime.Value < expectedUpdateDateTime.Value;

        if (!isMissed)
        {
            return;
        }

        await SendMissedScheduleNotification(dashboard, expectedUpdateDateTime.Value, timeSinceExpectedUpdate, homeAssistantService);
    }

    private async Task SendMissedScheduleNotification(
        Dashboard dashboard,
        DateTimeOffset expectedUpdateDateTime,
        TimeSpan timeSinceExpectedUpdate,
        HomeAssistantService homeAssistantService)
    {
        var message = $"Dashboard '{dashboard.Name}' has missed its scheduled update. " +
                     $"Expected update at {expectedUpdateDateTime:HH:mm} UTC ({timeSinceExpectedUpdate.TotalMinutes:F0} minutes ago). " +
                     $"Last successful update: {(dashboard.LastUpdateTime.HasValue ? dashboard.LastUpdateTime.Value.ToString("yyyy-MM-dd HH:mm") + " UTC" : "Never")}";

        _logger.LogWarning("Missed schedule detected for dashboard {DashboardName} (ID: {DashboardId}). " +
                         "Expected: {ExpectedTime}, Last Update: {LastUpdate}",
            dashboard.Name, dashboard.Id, expectedUpdateDateTime, dashboard.LastUpdateTime);

        var result = await homeAssistantService.SendNotification(
            dashboard,
            message,
            $"{Constants.AppName} - Missed Update");

        if (result.IsSuccess)
        {
            _logger.LogInformation("Notification sent successfully for missed schedule on dashboard {DashboardName}",
                dashboard.Name);
        }
        else
        {
            _logger.LogWarning("Failed to send notification for dashboard {DashboardName}: {Error}",
                dashboard.Name, result.Error);
        }
    }
}
