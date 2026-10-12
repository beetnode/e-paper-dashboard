using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using CSharpFunctionalExtensions;
using System.Security.Claims;

namespace EPaperDashboard.Services;

public class StandaloneStrategy : IDeploymentStrategy
{
    private readonly ILogger<StandaloneStrategy> _logger;
    private readonly IEnvironmentConfiguration _environmentConfiguration;

    public StandaloneStrategy(ILogger<StandaloneStrategy> logger, IEnvironmentConfiguration environmentConfiguration)
    {
        _logger = logger;
        _environmentConfiguration = environmentConfiguration;
    }

    public DeploymentMode Mode => DeploymentMode.Standalone;

    public bool IsUserManagementEnabled => true;

    public HomeAssistantEndpoint? DefaultHomeAssistant => null;

    public string GetConfigDirectory() => _environmentConfiguration.ConfigDir;

    public Uri? GetOAuthClientUri(HttpContext? context = null)
    {
        return _environmentConfiguration.ClientUri;
    }

    public UnitResult<string> ValidateConfiguration()
    {
        var missingConfigs = new List<string>();

        var clientUrlError = ClientUrlValidator.GetValidationError(_environmentConfiguration.ClientUri);
        if (clientUrlError is not null)
        {
            missingConfigs.Add(clientUrlError);
        }

        if (missingConfigs.Count > 0)
        {
            var message = $"""
                Invalid or missing configuration: {string.Join(", ", missingConfigs)}.
                Please set them as environment variables or in /data/options.json file.
                """;
            return UnitResult.Failure(message);
        }

        return UnitResult.Success<string>();
    }

    public ClaimsPrincipal? AuthenticateViaIngress(HttpContext context)
    {
        return null;
    }

    public Task<bool> ProcessIngressPathAsync(HttpContext context, IWebHostEnvironment environment)
    {
        return Task.FromResult(false);
    }

    public void PerformInitialSetup(IServiceProvider serviceProvider)
    {

    }

    public void ApplyMiddleware(IApplicationBuilder app, IWebHostEnvironment environment)
    {
    }

    public void ApplyPostAuthenticationMiddleware(IApplicationBuilder app, IWebHostEnvironment environment)
    {
    }

    public void ApplyPostStaticFilesMiddleware(IApplicationBuilder app, IWebHostEnvironment environment)
    {
    }
}
