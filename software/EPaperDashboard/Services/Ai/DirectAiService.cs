using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Ai;

public sealed class DirectAiService(
    IHttpClientFactory httpClientFactory,
    string endpoint,
    string? apiKey,
    string model,
    ILogger<DirectAiService> logger) : IAiService
{
    public async Task<Result<string>> GenerateCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default,
        bool jsonMode = true)
    {
        var firstAttempt = await TrySendRequestAsync(systemPrompt, userPrompt, jsonMode, cancellationToken);
        if (firstAttempt.IsSuccess)
        {
            return firstAttempt;
        }

        // Retry once for transient / rate-limit failures
        if (firstAttempt.Error.Contains("status 429") || firstAttempt.Error.Contains("status 5"))
        {
            logger.LogWarning("Retrying AI request after transient failure: {Error}", firstAttempt.Error);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return await TrySendRequestAsync(systemPrompt, userPrompt, jsonMode, cancellationToken);
        }

        return firstAttempt;
    }

    private async Task<Result<string>> TrySendRequestAsync(
        string systemPrompt,
        string userPrompt,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        object requestBody = jsonMode
            ? new { model, messages, temperature = 0.7, response_format = new { type = "json_object" } }
            : new { model, messages, temperature = 0.7 };

        var requestJson = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        // Normalize endpoint to end with /chat/completions
        var url = endpoint.TrimEnd('/');
        if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                url += "/v1";
            }
            url += "/chat/completions";
        }

        try
        {
            logger.LogInformation("Calling Direct AI endpoint: {Url} with model: {Model}", url, model);

            var response = await client.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Direct AI request failed with status {StatusCode}: {Body}",
                    response.StatusCode, responseBody);
                return Result.Failure<string>($"AI request failed with status {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices)
                && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var msgContent))
                {
                    var result = msgContent.GetString();
                    if (!string.IsNullOrEmpty(result))
                    {
                        logger.LogInformation("Direct AI returned response ({Length} chars)", result.Length);
                        return Result.Success<string>(result);
                    }
                }
            }

            return Result.Failure<string>("AI returned an empty or unexpected response");
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<string>("AI request was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Direct AI request failed");
            return Result.Failure<string>($"AI request failed: {ex.Message}");
        }
    }
}
