using EPaperDashboard.Models;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services;

public interface IDeploymentStrategy
{
    DeploymentMode Mode { get; }
    bool IsUserManagementEnabled { get; }
    /// <summary>
    /// The Home Assistant server that the deployment connects to by itself (the add-on's supervisor proxy).
    /// It backs every user's default Home Assistant connection; null when the deployment has none.
    /// </summary>
    HomeAssistantEndpoint? DefaultHomeAssistant { get; }
    /// <summary>Whether browser rendering runs inside the app instead of the restricted rendering sidecar.</summary>
    bool RunsRendererInProcess { get; }
    Result ValidateConfiguration();
    /// <summary>Whether the request came through Home Assistant ingress, which signs it in as the add-on's shared user.</summary>
    bool IsIngressRequest(HttpContext context);
    Result<HomeAssistantOAuthClient> GetHomeAssistantOAuthClient(HttpContext? context);
    void UseBeforeRouting(IApplicationBuilder app, IWebHostEnvironment environment);
    void UseBeforeStaticFiles(IApplicationBuilder app, IWebHostEnvironment environment);
}
