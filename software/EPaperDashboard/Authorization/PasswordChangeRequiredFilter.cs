using EPaperDashboard.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EPaperDashboard.Authorization;

/// <summary>
/// Keeps an account that was given a temporary password away from everything except signing out and choosing a new
/// password, so an administrator-issued password cannot become the account's long-term credential.
/// </summary>
public sealed class PasswordChangeRequiredFilter : IAuthorizationFilter
{
    public const string ErrorCode = "password_change_required";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.FindFirst(Constants.MustChangePasswordClaim)?.Value != "true"
            || context.ActionDescriptor.EndpointMetadata.OfType<AllowedDuringPasswordChangeAttribute>().Any())
        {
            return;
        }

        context.Result = new ObjectResult(new { code = ErrorCode, message = "Choose a new password to continue." })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
