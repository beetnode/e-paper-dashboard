using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Rendering;

public sealed class RenderingAccessService(
    IUserRepository users, IDashboardRepository dashboards, IDeploymentStrategy deployment, UserRenderScheduler scheduler)
{
    public bool IsEnabled(UserId owner)
    {
        if (deployment.Mode == DeploymentMode.Addon)
            return owner == Constants.HomeAssistantVirtualUserId && (users.GetRenderingEnabled(owner) ?? false);
        var user = users.FindById(owner);
        return user.HasValue && (users.GetRenderingEnabled(owner) ?? user.Value.RenderingEnabled ?? false);
    }

    public void RequireAccount(UserId owner)
    {
        if (deployment.Mode == DeploymentMode.Addon ? owner != Constants.HomeAssistantVirtualUserId : users.FindById(owner).HasNoValue)
            throw new RenderingRequestException("Account no longer exists.");
    }

    public void SetEnabled(UserId owner, bool enabled)
    {
        RequireAccount(owner);
        users.SetRenderingEnabled(owner, enabled);
        if (!enabled) scheduler.CancelUser(owner);
    }

    public void RequireAccess(UserId owner, DashboardId? dashboardId = null)
    {
        if (!IsEnabled(owner)) throw new RenderingRequestException("Enable rendering in Rendering settings.");
        if (dashboardId is { } id)
        {
            var dashboard = dashboards.FindById(id);
            if (dashboard.HasNoValue || dashboard.Value.UserId != owner)
                throw new RenderingRequestException("Dashboard is not accessible to this rendering account.");
        }
    }

    public async Task<T> RunAsync<T>(UserId owner, DashboardId? dashboardId, Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        RequireAccess(owner, dashboardId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var watcher = WatchAccessAsync(owner, dashboardId, lifetime);
        try
        {
            return await scheduler.RunAsync(owner, jobToken =>
            {
                RequireAccess(owner, dashboardId);
                return operation(jobToken);
            }, lifetime.Token);
        }
        finally
        {
            await lifetime.CancelAsync();
            await watcher;
        }
    }

    private async Task WatchAccessAsync(UserId owner, DashboardId? dashboardId, CancellationTokenSource lifetime)
    {
        try
        {
            // A preference change or account deletion on another replica must also cancel this job.
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(lifetime.Token)) RequireAccess(owner, dashboardId);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { await lifetime.CancelAsync(); }
    }
}
