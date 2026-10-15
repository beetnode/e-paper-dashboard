using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Ai;
using EPaperDashboard.Services.Providers;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public sealed class AiApiController(
    DashboardService dashboardService,
    UserService userService,
    AiDashboardGenerationService aiGenerationService,
    IAiContentProvider aiContentProvider,
    HomeAssistantConnectionService homeAssistantConnectionService) : BaseApiController
{
    [HttpGet("config")]
    public IActionResult GetGlobalAiConfig()
    {
        if (!userService.HasAccount(CurrentUserId))
        {
            return NotFound("User not found");
        }

        return Ok(userService.GetAiConfig(CurrentUserId) ?? new AiConfig());
    }

    [HttpPut("config")]
    public IActionResult UpdateGlobalAiConfig([FromBody] AiConfig config)
    {
        if (!userService.HasAccount(CurrentUserId))
        {
            return NotFound("User not found");
        }

        var validationError = ValidateGlobalAiConfig(config) ?? ValidateConnection(config);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }

        if (!userService.TryUpdateAiConfig(CurrentUserId, config))
        {
            return NotFound("User not found");
        }

        if (config.ConnectionMode == AiConnectionMode.None)
        {
            DisableAiOnDashboardsWithoutOverride(CurrentUserId);
        }

        return Ok(config);
    }

    private static string? ValidateGlobalAiConfig(AiConfig config)
    {
        return config.ConnectionMode switch
        {
            AiConnectionMode.Direct when string.IsNullOrWhiteSpace(config.DirectEndpoint)
                => "Direct endpoint URL is required",
            AiConnectionMode.Direct when string.IsNullOrWhiteSpace(config.DirectModel)
                => "Model name is required for direct connections",
            _ => null
        };
    }

    [HttpGet("models")]
    public async Task<IActionResult> GetAvailableModels([FromQuery] string endpoint, [FromQuery] string? apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return BadRequest("Endpoint is required");
        }

        var url = endpoint.TrimEnd('/');
        if (!url.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase))
        {
            if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                url += "/v1";
            }
            url += "/models";
        }

        try
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await client.GetAsync(url, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return BadRequest($"Failed to fetch models: HTTP {response.StatusCode}");
            }

            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;

            var models = new List<object>();

            // OpenAI-compatible format: { data: [{ id: "model-name", ... }] }
            if (root.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    if (id != null)
                    {
                        models.Add(new { id });
                    }
                }
            }
            // Ollama format: { models: [{ name: "model-name", ... }] }
            else if (root.TryGetProperty("models", out var ollamaModels) && ollamaModels.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in ollamaModels.EnumerateArray())
                {
                    var name = item.TryGetProperty("model", out var modelProp) ? modelProp.GetString()
                        : item.TryGetProperty("name", out var nameProp) ? nameProp.GetString()
                        : null;
                    if (name != null)
                    {
                        models.Add(new { id = name });
                    }
                }
            }

            return Ok(models.OrderBy(m => ((dynamic)m).id));
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, "Request to AI endpoint timed out");
        }
        catch (HttpRequestException ex)
        {
            return BadRequest($"Could not connect to endpoint: {ex.Message}");
        }
    }

    [HttpGet("dashboards/{dashboardId}/config")]
    public IActionResult GetAiConfig(string dashboardId)
    {
        var dashboard = GetOwnedDashboard(dashboardId);
        if (dashboard == null)
        {
            return NotFound("Dashboard not found");
        }

        return Ok(dashboard.AiConfig ?? new AiConfig());
    }

    [HttpPut("dashboards/{dashboardId}/config")]
    public IActionResult UpdateAiConfig(string dashboardId, [FromBody] AiConfig config)
    {
        var dashboard = GetOwnedDashboard(dashboardId);
        if (dashboard == null)
        {
            return NotFound("Dashboard not found");
        }

        var validationError = ValidateAiConfig(config) ?? ValidateConnection(config);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }

        dashboard.AiConfig = config;

        if (config.ConnectionMode == AiConnectionMode.None && dashboard.IsAiEnabled)
        {
            var globalConfig = userService.GetAiConfig(CurrentUserId);
            if (globalConfig == null || globalConfig.ConnectionMode == AiConnectionMode.None)
            {
                dashboard.IsAiEnabled = false;
            }
        }

        dashboardService.UpdateDashboard(dashboard);

        return Ok(config);
    }

    private string? ValidateConnection(AiConfig config) =>
        string.IsNullOrEmpty(config.HomeAssistantConnectionId)
        || homeAssistantConnectionService.FindConnection(config.HomeAssistantConnectionId, CurrentUserId) != null
            ? null
            : "Home Assistant connection not found";

    private static string? ValidateAiConfig(AiConfig config)
    {
        return config.ConnectionMode switch
        {
            AiConnectionMode.Direct
                => "Direct AI configuration is only supported as a global setting. Use the global AI Config page instead.",
            AiConnectionMode.HomeAssistant when string.IsNullOrWhiteSpace(config.HomeAssistantAgentId)
                => "Home Assistant conversation agent must be selected",
            _ => null
        };
    }

    [HttpPost("dashboards/{dashboardId}/generate")]
    public async Task<IActionResult> GenerateAiDashboard(
        string dashboardId,
        [FromBody] GenerateAiRequest? request,
        CancellationToken cancellationToken)
    {
        var dashboard = GetOwnedDashboard(dashboardId);
        if (dashboard == null)
        {
            return NotFound("Dashboard not found");
        }

        if (!dashboard.IsAiEnabled)
        {
            return BadRequest("AI is not enabled for this dashboard");
        }

        if (!HasEffectiveAiConfig(dashboard))
        {
            return BadRequest("AI is not configured. Set up an AI provider in Settings or the dashboard.");
        }

        var effectivePrompt = request?.Prompt ?? dashboard.AiPrompt;
        if (string.IsNullOrWhiteSpace(effectivePrompt))
        {
            return BadRequest("AI prompt is not configured for this dashboard");
        }

        var result = await aiGenerationService.GenerateAsync(
            dashboard, request?.Prompt, cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(new
            {
                widgets = result.Value.Widgets,
                generatedAt = dashboard.LastAiGenerationTime,
                dataSummary = result.Value.DataSummary,
                promptTokenEstimate = result.Value.PromptTokenEstimate
            });
        }

        return BadRequest(new { message = result.Error });
    }

    [HttpGet("dashboards/{dashboardId}/generated")]
    public IActionResult GetGeneratedWidgets(string dashboardId)
    {
        var dashboard = GetOwnedDashboard(dashboardId);
        if (dashboard == null)
        {
            return NotFound("Dashboard not found");
        }

        return Ok(new
        {
            widgets = dashboard.AiGeneratedWidgets ?? new List<WidgetConfig>(),
            generatedAt = dashboard.LastAiGenerationTime,
            isAiEnabled = dashboard.IsAiEnabled,
            prompt = dashboard.AiPrompt,
            lastError = dashboard.LastAiGenerationError
        });
    }

    [HttpDelete("dashboards/{dashboardId}/generated")]
    public IActionResult ClearGeneratedWidgets(string dashboardId)
    {
        var dashboard = GetOwnedDashboard(dashboardId);
        if (dashboard == null)
        {
            return NotFound("Dashboard not found");
        }

        dashboard.AiGeneratedWidgets = null;
        dashboard.LastAiGenerationTime = null;
        dashboardService.UpdateDashboard(dashboard);

        return NoContent();
    }

    [HttpGet("dashboards/{dashboardId}/conversation-agents")]
    public async Task<IActionResult> GetConversationAgents(string dashboardId, [FromQuery] string? connectionId, CancellationToken cancellationToken)
    {
        var dashboard = GetOwnedDashboard(dashboardId);
        if (dashboard == null)
        {
            return NotFound("Dashboard not found");
        }

        // The query names the connection being chosen; otherwise use the one already saved for this dashboard.
        var connection = homeAssistantConnectionService.ResolveConnection(dashboard, connectionId ?? dashboard.AiConfig?.HomeAssistantConnectionId);
        var connectionInfo = connection.Bind(homeAssistantConnectionService.GetConnectionInfo);
        if (connectionInfo.IsFailure)
        {
            return BadRequest("Select a Home Assistant connection that is signed in to list its conversation agents.");
        }

        try
        {
            using var ws = await homeAssistantConnectionService.ConnectAsync(connection.Value);

            var message = new Dictionary<string, object>
            {
                ["id"] = homeAssistantConnectionService.NextMessageId(),
                ["type"] = "conversation/agent/list",
                ["language"] = "en"
            };

            await HomeAssistantConnectionService.SendMessageAsync(ws, message);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            var responseStr = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, timeoutCts.Token);

            await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);

            using var doc = JsonDocument.Parse(responseStr);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var success) && success.GetBoolean()
                && root.TryGetProperty("result", out var result)
                && result.TryGetProperty("agents", out var agents))
            {
                var agentList = new List<object>();
                foreach (var agent in agents.EnumerateArray())
                {
                    var id = agent.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    var name = agent.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (id is not null)
                    {
                        agentList.Add(new { id, name = name ?? id });
                    }
                }
                return Ok(agentList);
            }

            return Ok(Array.Empty<object>());
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, "Home Assistant request timed out");
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to fetch conversation agents: {ex.Message}");
        }
    }

    [HttpPost("dashboards/{dashboardId}/widgets/{widgetId}/generate-content")]
    public async Task<IActionResult> GenerateWidgetContent(
        string dashboardId, string widgetId, CancellationToken cancellationToken)
    {
        var dashboard = GetOwnedDashboard(dashboardId);
        if (dashboard == null)
            return NotFound("Dashboard not found");

        var widget = dashboard.LayoutConfig?.Widgets?.FirstOrDefault(w => w.Id == widgetId);
        if (widget == null || widget.Type != "ai-content")
            return NotFound("AI content widget not found");

        var prompt = widget.Config.TryGetProperty("prompt", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(prompt))
            return BadRequest("Widget has no prompt configured");

        var result = await aiContentProvider.GenerateAndCacheContentAsync(
            dashboardId, widgetId, prompt, cancellationToken);

        if (result.IsFailure)
            return BadRequest(result.Error);

        return Ok(new { content = result.Value });
    }

    [HttpGet("dashboards/{dashboardId}/widgets/{widgetId}/content")]
    public IActionResult GetWidgetContent(string dashboardId, string widgetId)
    {
        var dashboard = GetOwnedDashboard(dashboardId);
        if (dashboard == null)
            return NotFound("Dashboard not found");

        var widget = dashboard.LayoutConfig?.Widgets?.FirstOrDefault(w => w.Id == widgetId);
        if (widget == null || widget.Type != "ai-content")
            return NotFound("AI content widget not found");

        return Ok(new { content = aiContentProvider.GetCachedContent(dashboardId, widgetId) });
    }

    private Dashboard? GetOwnedDashboard(string dashboardId)
    {
        if (!DashboardId.TryParse(dashboardId, out var id))
        {
            return null;
        }

        var dashboard = dashboardService.GetDashboardById(id);
        if (dashboard is null || dashboard.UserId != CurrentUserId)
        {
            return null;
        }

        return dashboard;
    }

    private void DisableAiOnDashboardsWithoutOverride(UserId userId)
    {
        var dashboards = dashboardService.GetDashboardsForUser(userId);
        foreach (var dashboard in dashboards)
        {
            if (!dashboard.IsAiEnabled)
            {
                continue;
            }

            var hasOverride = dashboard.AiConfig != null
                && dashboard.AiConfig.ConnectionMode == AiConnectionMode.HomeAssistant;
            if (!hasOverride)
            {
                dashboard.IsAiEnabled = false;
                dashboardService.UpdateDashboard(dashboard);
            }
        }
    }

    private bool HasEffectiveAiConfig(Dashboard dashboard)
    {
        if (dashboard.AiConfig != null && dashboard.AiConfig.ConnectionMode == AiConnectionMode.HomeAssistant)
        {
            return true;
        }

        var globalConfig = userService.GetAiConfig(dashboard.UserId);
        return globalConfig != null && globalConfig.ConnectionMode != AiConnectionMode.None;
    }
}

public record GenerateAiRequest(string? Prompt = null);
