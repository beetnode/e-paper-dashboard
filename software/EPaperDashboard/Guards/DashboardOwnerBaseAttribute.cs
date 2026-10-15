using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Guards;

/// <summary>
/// Base class for dashboard ownership authorization filters.
/// Contains common validation logic for checking if the authenticated user owns a dashboard.
/// </summary>
public abstract class DashboardOwnerBaseAttribute : Attribute
{
    /// <summary>
    /// Validates that the current user owns the specified dashboard.
    /// </summary>
    /// <param name="context">The authorization filter context</param>
    /// <param name="dashboardId">The dashboard ID to validate</param>
    protected void ValidateDashboardOwnership(AuthorizationFilterContext context, string? dashboardId)
    {
        if (string.IsNullOrWhiteSpace(dashboardId))
        {
            context.Result = new BadRequestObjectResult(new { error = "Dashboard ID is required" });
            return;
        }

        var userId = context.HttpContext.User.GetUserId();
        if (userId == UserId.Empty)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Get services from DI
        var userService = context.HttpContext.RequestServices.GetService<UserService>();
        var dashboardService = context.HttpContext.RequestServices.GetService<DashboardService>();

        if (userService == null || dashboardService == null)
        {
            context.Result = new StatusCodeResult(500);
            return;
        }

        if (!userService.HasAccount(userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Parse and validate dashboard ID
        if (!DashboardId.TryParse(dashboardId, out var dashboardIdTyped))
        {
            context.Result = new BadRequestObjectResult(new { error = "Invalid dashboard ID." });
            return;
        }

        // Get dashboard
        var dashboardMaybe = dashboardService.GetDashboardById(dashboardIdTyped);
        if (dashboardMaybe is null)
        {
            context.Result = new NotFoundObjectResult(new { error = "Dashboard not found." });
            return;
        }

        // Check ownership
        if (dashboardMaybe.UserId != userId)
        {
            context.Result = new ForbidResult();
            return;
        }

        // Authorization successful - context.Result remains null, continue to action
    }
}

