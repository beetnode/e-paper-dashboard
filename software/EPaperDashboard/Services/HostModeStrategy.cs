using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using CSharpFunctionalExtensions;
using System.Security.Claims;

namespace EPaperDashboard.Services;

public class HostModeStrategy : IDeploymentStrategy
{
    private readonly ILogger<HostModeStrategy> _logger;
    private readonly IEnvironmentConfiguration _environmentConfiguration;
    private readonly string _homeAssistantHost;

    public HostModeStrategy(ILogger<HostModeStrategy> logger, IEnvironmentConfiguration environmentConfiguration)
    {
        _logger = logger;
        _environmentConfiguration = environmentConfiguration;
        _homeAssistantHost = environmentConfiguration.HomeAssistantHost
            ?? Constants.HomeAssistantCoreUrl;
    }

    public DeploymentMode Mode => DeploymentMode.Host;

    public bool IsUserManagementEnabled => true;

    public bool IsAutoConnected => false;

    public string WebSocketPath => "/api/websocket";

    public string GetConfigDirectory() => _environmentConfiguration.ConfigDir;

    public Uri? GetOAuthClientUri(HttpContext? context = null)
    {
        return _environmentConfiguration.ClientUri;
    }

    public Task<string?> CreateAccessTokenAsync(string clientName)
    {
        return Task.FromResult<string?>(null);
    }

    public (string host, string token) GetHomeAssistantConnection(Dashboard dashboard)
    {
        return (_homeAssistantHost, dashboard.AccessToken!);
    }

    public UnitResult<string> ValidateConfiguration()
    {
        var missingConfigs = new List<string>();

        var clientUrlError = ClientUrlValidator.GetValidationError(_environmentConfiguration.ClientUri);
        if (clientUrlError is not null)
            missingConfigs.Add(clientUrlError);

        if (string.IsNullOrWhiteSpace(_environmentConfiguration.StateSigningKey))
            missingConfigs.Add("STATE_SIGNING_KEY");

        if (missingConfigs.Count > 0)
        {
            return UnitResult.Failure(
                $"Invalid or missing configuration: {string.Join(", ", missingConfigs)}. " +
                "Please set them as environment variables or in /data/options.json file.");
        }

        return UnitResult.Success<string>();
    }

    public ClaimsPrincipal? AuthenticateViaIngress(HttpContext context) => null;

    public Task<bool> ProcessIngressPathAsync(HttpContext context, IWebHostEnvironment environment)
        => Task.FromResult(false);

    public void PerformInitialSetup(IServiceProvider serviceProvider)
    {

    }

    public void ApplyMiddleware(IApplicationBuilder app, IWebHostEnvironment environment) { }
    public void ApplyPostAuthenticationMiddleware(IApplicationBuilder app, IWebHostEnvironment environment) { }
    public void ApplyPostStaticFilesMiddleware(IApplicationBuilder app, IWebHostEnvironment environment) { }
}
