using System.Security.Claims;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EPaperDashboard.UnitTests.TestSupport;

/// <summary>
/// Attaches a fake authenticated <see cref="ClaimsPrincipal"/> to a controller under test,
/// mirroring the claims <see cref="EPaperDashboard.Controllers.BaseApiController"/> reads
/// (user id, super-user flag, Home Assistant ingress flag) without needing real auth middleware.
/// </summary>
internal static class ControllerTestHelpers
{
    public static T WithUser<T>(
        this T controller,
        UserId? userId = null,
        bool isSuperUser = false,
        bool isHomeAssistantIngress = false) where T : ControllerBase
    {
        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.Value));
        }
        if (isSuperUser)
        {
            claims.Add(new Claim(Constants.IsSuperUserClaim, "true"));
        }
        if (isHomeAssistantIngress)
        {
            claims.Add(new Claim(Constants.HomeAssistantIngressClaim, "true"));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    /// <summary>
    /// Simulates a Home Assistant ingress request, authenticated as the add-on's HomeAssistantVirtualUserId.
    /// </summary>
    public static T WithHomeAssistantIngressUser<T>(this T controller) where T : ControllerBase
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Constants.HomeAssistantVirtualUserId.ToString()),
            new(Constants.HomeAssistantIngressClaim, "true")
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };
        return controller;
    }
}
