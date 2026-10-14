using System.Security.Claims;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EPaperDashboard.Authentication;

/// <summary>Builds the cookie principal for a local account, so every sign-in issues the same claims.</summary>
public static class UserClaims
{
    public static ClaimsPrincipal Create(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(Constants.IsSuperUserClaim, user.IsSuperUser ? "true" : "false")
        };
        if (!string.IsNullOrEmpty(user.SecurityStamp))
        {
            claims.Add(new Claim(Constants.SecurityStampClaim, user.SecurityStamp));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
