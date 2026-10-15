using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Ai;

namespace EPaperDashboard.Services.Providers;

public sealed class AiContentProvider(
    DashboardService dashboardService,
    UserService userService,
    IAiServiceFactory aiServiceFactory,
    AiDataFetcher dataFetcher,
    IEnumerable<IAiDataSectionFormatter> sectionFormatters,
    TimeProvider timeProvider,
    ILogger<AiContentProvider> logger) : IAiContentProvider
{
    private const string SystemPrompt = """
        You are an e-paper dashboard content writer. Generate content based on the user's prompt.
        Return ONLY the content text — no JSON wrapping, no code fences.

        You will receive the user's prompt along with available smart-home data (entity states,
        weather forecasts, calendar events, todo lists, RSS feeds). Use this data when the prompt
        references it. Ignore data that is not relevant to the prompt.

        ## Formatting
        The content is rendered as markdown. Supported syntax:
        - Headings: # H1, ## H2, ### H3, #### H4
        - Emphasis: **bold**, *italic*, ~~strikethrough~~
        - Lists: unordered (- item), ordered (1. item), nested (indent with spaces)
        - Task lists: - [ ] pending, - [x] completed
        - Blockquotes: > text
        - Horizontal rules: ---
        - Fenced code blocks: ```code```

        ## Icons
        You can embed Font Awesome solid icons inline using the :fa-icon-name: syntax.
        Examples: :fa-sun: :fa-cloud: :fa-house: :fa-calendar: :fa-check: :fa-star:
        Only Font Awesome solid icons are available. Use them to add visual cues.

        ## Constraints
        - Images (![alt](url)) are NOT supported — use :fa-icon: icons for visual elements.
        - HTML tags are NOT supported and will be stripped. Use only markdown syntax.
        - Keep content concise and suitable for a small e-paper display widget.
        - Prefer short paragraphs and lists over long prose.
        """;

    public async Task<Result<string, string>> GenerateContentAsync(
        string dashboardId, string prompt, CancellationToken cancellationToken)
    {
        if (!DashboardId.TryParse(dashboardId, out var id))
            return Result.Failure<string, string>("Invalid dashboard ID");

        var dashboard = dashboardService.GetDashboardById(id);
        if (dashboard is null)
            return Result.Failure<string, string>("Dashboard not found");

        return await CallAiAsync(
            dashboard,
            dashboardId,
            prompt,
            includeDashboardData: true,
            cancellationToken);
    }

    public async Task<Result<string, string>> GenerateAndCacheContentAsync(
        string dashboardId, string widgetId, string prompt, CancellationToken cancellationToken)
    {
        if (!DashboardId.TryParse(dashboardId, out var id))
            return Result.Failure<string, string>("Invalid dashboard ID");

        var dashboard = dashboardService.GetDashboardById(id);
        if (dashboard is null)
            return Result.Failure<string, string>("Dashboard not found");

        var result = await CallAiAsync(
            dashboard,
            dashboardId,
            prompt,
            ShouldIncludeDashboardData(dashboard, widgetId),
            cancellationToken);
        if (result.IsSuccess)
        {
            StoreInCache(dashboard, widgetId, result.Value);
        }

        return result;
    }

    public async Task PreGenerateAllAsync(string dashboardId, CancellationToken cancellationToken)
    {
        if (!DashboardId.TryParse(dashboardId, out var id))
            return;

        var dashboard = dashboardService.GetDashboardById(id);
        if (dashboard is null || dashboard.LayoutConfig?.Widgets == null)
            return;

        var aiWidgets = dashboard.LayoutConfig.Widgets
            .Where(w => w.Type == "ai-content")
            .ToList();

        if (aiWidgets.Count == 0)
            return;

        var effectiveConfig = ResolveAiConfig(dashboard);
        if (effectiveConfig == null || effectiveConfig.ConnectionMode == AiConnectionMode.None)
            return;

        logger.LogInformation(
            "Pre-generating AI content for {Count} widget(s) on dashboard {DashboardId}",
            aiWidgets.Count, dashboardId);

        var anyUpdated = false;
        foreach (var widget in aiWidgets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prompt = GetStringProp(widget.Config, "prompt");
            if (string.IsNullOrWhiteSpace(prompt))
                continue;

            var result = await CallAiAsync(
                dashboard,
                dashboardId,
                prompt,
                GetStringProp(widget.Config, "dataScope") != "none",
                cancellationToken);
            if (result.IsSuccess)
            {
                dashboard.AiContentCache ??= new Dictionary<string, string>();
                dashboard.AiContentCache[widget.Id] = result.Value;
                anyUpdated = true;

                logger.LogInformation(
                    "Pre-generated AI content for widget {WidgetId} ({Length} chars)",
                    widget.Id, result.Value.Length);
            }
            else
            {
                logger.LogWarning(
                    "Failed to pre-generate AI content for widget {WidgetId}: {Error}",
                    widget.Id, result.Error);
            }
        }

        if (anyUpdated)
        {
            dashboard.LastAiContentCacheTime = timeProvider.GetUtcNow();
            dashboardService.UpdateDashboard(dashboard);
        }
    }

    public string? GetCachedContent(string dashboardId, string widgetId)
    {
        if (!DashboardId.TryParse(dashboardId, out var id))
            return null;

        var dashboard = dashboardService.GetDashboardById(id);
        if (dashboard is null)
            return null;

        return dashboard.AiContentCache?.GetValueOrDefault(widgetId);
    }

    private async Task<Result<string, string>> CallAiAsync(
        Dashboard dashboard,
        string dashboardId,
        string prompt,
        bool includeDashboardData,
        CancellationToken cancellationToken)
    {
        var effectiveConfig = ResolveAiConfig(dashboard);
        if (effectiveConfig == null || effectiveConfig.ConnectionMode == AiConnectionMode.None)
        {
            logger.LogWarning("AI not configured for dashboard {DashboardId}, skipping ai-content generation", dashboardId);
            return Result.Failure<string, string>("AI is not configured");
        }

        var aiServiceResult = aiServiceFactory.Create(effectiveConfig, dashboardId);
        if (aiServiceResult.IsFailure)
            return Result.Failure<string, string>(aiServiceResult.Error);

        var aiData = includeDashboardData
            ? await dataFetcher.FetchAsync(dashboardId, cancellationToken)
            : new AiDataSnapshot();
        var userPrompt = BuildUserPrompt(prompt, aiData, sectionFormatters);

        return await aiServiceResult.Value.GenerateCompletionAsync(
            SystemPrompt, userPrompt, cancellationToken, jsonMode: false);
    }

    private void StoreInCache(Dashboard dashboard, string widgetId, string content)
    {
        dashboard.AiContentCache ??= new Dictionary<string, string>();
        dashboard.AiContentCache[widgetId] = content;
        dashboard.LastAiContentCacheTime = timeProvider.GetUtcNow();
        dashboardService.UpdateDashboard(dashboard);
    }

    private string BuildUserPrompt(string prompt, AiDataSnapshot data, IEnumerable<IAiDataSectionFormatter> formatters)
    {
        var sb = new StringBuilder();
        var now = timeProvider.GetLocalNow();

        sb.AppendLine($"Current date/time: {now:dddd, MMMM d, yyyy h:mm tt}");
        sb.AppendLine();
        sb.AppendLine("## Prompt");
        sb.AppendLine(prompt);
        sb.AppendLine();

        var activeSections = formatters.Where(f => f.HasData(data)).ToList();
        if (activeSections.Count == 0)
            return sb.ToString();

        sb.AppendLine("## Available Data");
        sb.AppendLine();

        foreach (var section in activeSections)
        {
            sb.Append(section.FormatSection(data));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private AiConfig? ResolveAiConfig(Dashboard dashboard)
    {
        if (dashboard.AiConfig != null && dashboard.AiConfig.ConnectionMode == AiConnectionMode.HomeAssistant)
        {
            return dashboard.AiConfig;
        }

        return userService.GetAiConfig(dashboard.UserId);
    }

    private static string? GetStringProp(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool ShouldIncludeDashboardData(Dashboard dashboard, string widgetId)
    {
        var widget = dashboard.LayoutConfig?.Widgets.FirstOrDefault(item => item.Id == widgetId);
        return widget is null || GetStringProp(widget.Config, "dataScope") != "none";
    }
}
