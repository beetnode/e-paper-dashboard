using System.Text.Json;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Ai;

public sealed class HomeAssistantAiService(
    string hostUrl,
    string token,
    string webSocketPath,
    string? agentId,
    ILogger<HomeAssistantAiService> logger) : IAiService
{
    public async Task<Result<string>> GenerateCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default,
        bool jsonMode = true)
    {
        try
        {
            // Combine system and user prompts for the conversation API
            var combinedPrompt = $"{systemPrompt}\n\n{userPrompt}";

            logger.LogInformation("Calling Home Assistant conversation/process API (agent: {AgentId})",
                agentId ?? "default");

            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, webSocketPath);

            var message = new Dictionary<string, object?>
            {
                ["id"] = 2,
                ["type"] = "conversation/process",
                ["text"] = combinedPrompt,
                ["language"] = "en"
            };

            if (!string.IsNullOrWhiteSpace(agentId))
            {
                message["agent_id"] = agentId;
            }

            await HomeAssistantConnectionService.SendMessageAsync(ws, message);

            // Use a timeout to prevent hanging forever on slow AI responses
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
            var responseStr = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, timeoutCts.Token);

            await ws.CloseAsync(
                System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                "Done",
                CancellationToken.None);

            using var doc = JsonDocument.Parse(responseStr);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var success) && success.GetBoolean()
                && root.TryGetProperty("result", out var result))
            {
                if (result.TryGetProperty("response", out var response)
                    && response.TryGetProperty("speech", out var speech)
                    && speech.TryGetProperty("plain", out var plain)
                    && plain.TryGetProperty("speech", out var speechText))
                {
                    var text = speechText.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        logger.LogInformation("HA AI returned response ({Length} chars)", text.Length);
                        return Result.Success<string>(text);
                    }
                }
            }

            if (root.TryGetProperty("error", out var error))
            {
                var errorMessage = error.TryGetProperty("message", out var msg)
                    ? msg.GetString() ?? "Unknown error"
                    : "Unknown error";
                logger.LogError("HA conversation API error: {Error}", errorMessage);
                return Result.Failure<string>($"Home Assistant AI error: {errorMessage}");
            }

            logger.LogWarning("HA AI returned unexpected response: {Response}", responseStr);
            return Result.Failure<string>("Home Assistant AI returned an unexpected response");
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<string>("AI request was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Home Assistant AI request failed");
            return Result.Failure<string>($"Home Assistant AI request failed: {ex.Message}");
        }
    }
}
