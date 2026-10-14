using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services;

public static class DeploymentStrategyFactory
{
    public static IDeploymentStrategy Create(
        DeploymentMode mode,
        IEnvironmentConfiguration environmentConfiguration,
        ILoggerFactory loggerFactory) => mode switch
    {
        DeploymentMode.Addon => new HomeAssistantAddonStrategy(
            loggerFactory.CreateLogger<HomeAssistantAddonStrategy>(), environmentConfiguration),
        _ => new StandaloneStrategy(
            loggerFactory.CreateLogger<StandaloneStrategy>(), environmentConfiguration)
    };
}
