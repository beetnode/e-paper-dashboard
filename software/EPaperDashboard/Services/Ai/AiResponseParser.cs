using System.Text.Json;
using EPaperDashboard.Utilities;
using EPaperDashboard.Models;

namespace EPaperDashboard.Services.Ai;

public sealed class AiResponseParser(ILogger<AiResponseParser> logger)
{
    public Result<List<WidgetConfig>> Parse(string response)
    {
        try
        {
            var json = StripCodeFences(response);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("widgets", out var widgetsArray)
                || widgetsArray.ValueKind != JsonValueKind.Array)
            {
                return "AI response does not contain a 'widgets' array";
            }

            var widgets = new List<WidgetConfig>();
            var typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var w in widgetsArray.EnumerateArray())
            {
                var type = w.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

                if (string.IsNullOrEmpty(type))
                {
                    continue;
                }

                if (!IsKnownWidgetType(type))
                {
                    logger.LogWarning("AI generated unknown widget type '{Type}', skipping", type);
                    continue;
                }

                typeCounts.TryGetValue(type, out var count);
                typeCounts[type] = count + 1;
                var id = count == 0 ? type : $"{type}-{count + 1}";

                var config = w.TryGetProperty("config", out var configEl)
                    ? configEl.Clone()
                    : JsonSerializer.SerializeToElement(new { });

                string? titleOverride = w.TryGetProperty("titleOverride", out var toEl)
                    ? toEl.GetString()
                    : null;

                widgets.Add(new WidgetConfig
                {
                    Id = id,
                    Type = type,
                    Position = new WidgetPosition(),
                    Config = config,
                    TitleOverride = titleOverride
                });
            }

            if (widgets.Count == 0)
            {
                return "AI generated no valid widgets";
            }

            return widgets;
        }
        catch (JsonException ex)
        {
            return $"AI response is not valid JSON: {ex.Message}";
        }
    }

    public async Task<Result<List<WidgetConfig>>> RepairAndParseAsync(
        IAiService aiService,
        string brokenResponse,
        string parseError,
        CancellationToken cancellationToken)
    {
        const string repairSystemPrompt = """
            You are a JSON repair tool. The user will give you a broken JSON response and the parse error.
            Fix the JSON so it is valid. Return ONLY the corrected JSON — no markdown, no explanation, no code fences.
            The JSON must be an object with a "widgets" array: {"widgets": [...]}
            Do NOT change the meaning of the data — only fix syntax errors (missing commas, brackets, quotes, trailing commas, etc.).
            """;

        var repairUserPrompt = $"""
            ## Parse Error
            {parseError}

            ## Broken JSON
            {brokenResponse}
            """;

        var repairResult = await aiService.GenerateCompletionAsync(
            repairSystemPrompt, repairUserPrompt, cancellationToken);

        if (repairResult.IsFailure)
        {
            return $"Repair LLM call failed: {repairResult.Error}";
        }

        var repairedParseResult = Parse(repairResult.Value);
        if (repairedParseResult.IsFailure)
        {
            return $"Repaired JSON still invalid: {repairedParseResult.Error}";
        }

        logger.LogInformation("JSON repair pass succeeded, recovered {Count} widgets", repairedParseResult.Value.Count);
        return repairedParseResult;
    }

    private static string StripCodeFences(string response)
    {
        var json = response.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline >= 0)
            {
                json = json[(firstNewline + 1)..];
            }
            if (json.EndsWith("```"))
            {
                json = json[..^3];
            }
            json = json.Trim();
        }
        return json;
    }

    public static bool IsKnownWidgetType(string type) =>
        type is "header" or "markdown" or "calendar" or "weather" or "weather-forecast"
            or "todo" or "rss-feed" or "graph" or "app-icon" or "ai-content";
}
