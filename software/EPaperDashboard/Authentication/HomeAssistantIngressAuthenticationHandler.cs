using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EPaperDashboard.Utilities;
using EPaperDashboard.Services;

namespace EPaperDashboard.Authentication;

/// <summary>
/// Signs in requests that the deployment recognizes as Home Assistant ingress as the add-on's shared user.
/// </summary>
public sealed class HomeAssistantIngressAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDeploymentStrategy deploymentStrategy) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "HomeAssistantIngress";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!deploymentStrategy.IsIngressRequest(Context))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Constants.HomeAssistantVirtualUserId.ToString()),
            new Claim(ClaimTypes.Name, Constants.HomeAssistantAdminUserName),
            new Claim(Constants.IsSuperUserClaim, "true"),
            new Claim(Constants.HomeAssistantIngressClaim, "true")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
