using EPaperDashboard.Models;

namespace EPaperDashboard.Utilities;

/// <summary>
/// Fixed per-account limits that keep one account from exhausting a shared instance. Items created before a limit
/// existed are kept; only new ones are refused. The add-on's single shared owner is not limited.
/// </summary>
public static class UserLimits
{
    public const int MaxDashboards = 50;
    public const int MaxDevices = 20;
    public const int MaxConnections = 20;

    public static bool AppliesTo(UserId owner) => owner != Constants.HomeAssistantVirtualUserId;
}
