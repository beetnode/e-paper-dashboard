using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Controllers;

/// <summary>
/// Base controller providing common functionality for all API controllers.
/// </summary>
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Gets the current user's ID from claims (the add-on's ingress user included).
    /// Returns UserId.Empty if not authenticated or claim not found.
    /// </summary>
    protected UserId CurrentUserId => User.GetUserId();

    /// <summary>
    /// Gets whether the current user is a super user.
    /// </summary>
    protected bool IsSuperUser => 
        User.FindFirst(Constants.IsSuperUserClaim)?.Value == "true";

    /// <summary>
    /// Gets whether the current user is authenticated via Home Assistant ingress.
    /// </summary>
    protected bool IsHomeAssistantIngress => 
        User.FindFirst(Constants.HomeAssistantIngressClaim)?.Value == "true";

    /// <summary>
    /// Gets the current user's username from claims.
    /// </summary>
    protected string? CurrentUsername => 
        User.FindFirst(ClaimTypes.Name)?.Value;
}

