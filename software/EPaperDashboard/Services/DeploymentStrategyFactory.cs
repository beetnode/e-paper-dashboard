using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services;

public static class DeploymentStrategyFactory
{
    public static IDeploymentStrategy Create(
        DeploymentMode mode,
        IEnvironmentConfiguration environmentConfiguration,
        ILoggerFactory loggerFactory,
        int devicePort = Constants.DefaultDevicePort,
        bool allowLoopbackIngress = false) => mode switch
    {
        DeploymentMode.Addon => new HomeAssistantAddonStrategy(
            loggerFactory.CreateLogger<HomeAssistantAddonStrategy>(), environmentConfiguration, devicePort, allowLoopbackIngress),
        _ => new StandaloneStrategy(
            loggerFactory.CreateLogger<StandaloneStrategy>(), environmentConfiguration)
    };
}
