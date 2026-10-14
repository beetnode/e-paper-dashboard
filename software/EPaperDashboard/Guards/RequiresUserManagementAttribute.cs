using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using EPaperDashboard.Services;

namespace EPaperDashboard.Guards;

/// <summary>
/// Rejects requests to local account endpoints when the deployment has no local accounts
/// (the add-on signs everyone in through Home Assistant ingress).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresUserManagementAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var deploymentStrategy = context.HttpContext.RequestServices.GetService<IDeploymentStrategy>();
        if (deploymentStrategy is { IsUserManagementEnabled: false })
        {
            context.Result = new ObjectResult(new { message = "User management is disabled in Home Assistant add-on mode" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
