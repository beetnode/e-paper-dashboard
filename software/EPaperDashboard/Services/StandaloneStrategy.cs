using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using CSharpFunctionalExtensions;

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

    public bool RunsRendererInProcess => false;

    public string GetConfigDirectory() => _environmentConfiguration.ConfigDir;

    public Result<HomeAssistantOAuthClient, string> GetHomeAssistantOAuthClient(HttpContext? context)
    {
        if (_environmentConfiguration.ClientUri is not { } clientUri)
        {
            return "CLIENT_URL is not configured. This is required for Home Assistant sign-in.";
        }

        var appUrl = clientUri.ToString().TrimEnd('/');
        return new HomeAssistantOAuthClient(appUrl, appUrl);
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

    public bool IsIngressRequest(HttpContext context) => false;

    public void PerformInitialSetup(IServiceProvider serviceProvider)
    {

    }

    public void UseBeforeRouting(IApplicationBuilder app, IWebHostEnvironment environment)
    {
    }

    public void UseBeforeStaticFiles(IApplicationBuilder app, IWebHostEnvironment environment)
    {
    }
}
