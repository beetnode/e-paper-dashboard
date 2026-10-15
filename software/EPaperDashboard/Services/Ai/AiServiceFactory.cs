using EPaperDashboard.Utilities;
using EPaperDashboard.Models;

namespace EPaperDashboard.Services.Ai;

public interface IAiServiceFactory
{
    Result<IAiService> Create(AiConfig aiConfig, string? dashboardId = null);
}

public sealed class AiServiceFactory(
    IHttpClientFactory httpClientFactory,
    HomeAssistantConnectionService homeAssistantConnectionService,
    ILoggerFactory loggerFactory) : IAiServiceFactory
{
    public Result<IAiService> Create(AiConfig aiConfig, string? dashboardId = null)
    {
        return aiConfig.ConnectionMode switch
        {
            AiConnectionMode.Direct => CreateDirectService(aiConfig),
            AiConnectionMode.HomeAssistant => CreateHomeAssistantService(aiConfig, dashboardId),
            _ => Result.Failure<IAiService>("AI is not configured. Set up an AI connection in user settings.")
        };
    }

    private Result<IAiService> CreateDirectService(AiConfig aiConfig)
    {
        if (string.IsNullOrWhiteSpace(aiConfig.DirectEndpoint))
        {
            return "Direct AI endpoint is required";
        }
        if (string.IsNullOrWhiteSpace(aiConfig.DirectModel))
        {
            return "Direct AI model name is required";
        }

        return new DirectAiService(
            httpClientFactory,
            aiConfig.DirectEndpoint,
            aiConfig.DirectApiKey,
            aiConfig.DirectModel,
            loggerFactory.CreateLogger<DirectAiService>());
    }

    private Result<IAiService> CreateHomeAssistantService(AiConfig aiConfig, string? dashboardId)
    {
        if (string.IsNullOrWhiteSpace(dashboardId))
        {
            return "A dashboard with Home Assistant connection is required for HA AI mode";
        }

        var connectionInfo = homeAssistantConnectionService.ResolveConnection(dashboardId, aiConfig.HomeAssistantConnectionId)
            .Bind(homeAssistantConnectionService.GetConnectionInfo);
        if (connectionInfo.IsFailure)
        {
            return connectionInfo.Error;
        }

        return new HomeAssistantAiService(
            connectionInfo.Value.HostUrl,
            connectionInfo.Value.Token,
            connectionInfo.Value.WebSocketPath,
            aiConfig.HomeAssistantAgentId,
            loggerFactory.CreateLogger<HomeAssistantAiService>());
    }
}
