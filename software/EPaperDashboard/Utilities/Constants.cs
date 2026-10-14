
using EPaperDashboard.Models;

namespace EPaperDashboard.Utilities;

public static class Constants
{
    public const string AppName = "izBoard";

    public const string CompanyName = "beetnode";

    public static string AppVersion { get; } = GetAppVersion();

    private static string GetAppVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        
        return version?.ToString() ?? "0.0.0";
    }

    public const string DashboardHttpClientName = nameof(DashboardHttpClientName);

    public const string HassHttpClientName = nameof(HassHttpClientName);

    public const string FirmwareHttpClientName = nameof(FirmwareHttpClientName);

    public const string PlaywrightComponentHttpClientName = nameof(PlaywrightComponentHttpClientName);

    public const string SsrImageHttpClientName = nameof(SsrImageHttpClientName);

    public const string SupervisorCoreUrl = "http://supervisor/core";

    public const string IngressPathHeader = "X-Ingress-Path";
    
    public const string IsSuperUserClaim = "IsSuperUser";
    public const string HomeAssistantIngressClaim = "HomeAssistantIngress";
    public const string HomeAssistantAdminUserName = "Home Assistant Admin";
    
    /// <summary>
    /// The add-on's ingress user. It has no user record; <see cref="Services.UserService"/> is the only place
    /// that knows how its account and settings are stored.
    /// </summary>
    public static readonly UserId HomeAssistantVirtualUserId = new UserId("000000000000000000000001");
}
