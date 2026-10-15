using System.Security.Claims;
using EPaperDashboard.Models;

namespace EPaperDashboard.Utilities;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Returns the authenticated user's ID, or UserId.Empty when there is none.</summary>
    public static UserId GetUserId(this ClaimsPrincipal user) =>
        UserId.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : UserId.Empty;
}
