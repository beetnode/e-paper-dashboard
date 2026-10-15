using EPaperDashboard.Utilities;
using EPaperDashboard.Models;

namespace EPaperDashboard.Services.Ai;

public sealed class AiDashboardGenerationService(
    IAiServiceFactory aiServiceFactory,
    AiDataFetcher dataFetcher,
    AiResponseParser responseParser,
    WidgetValidator widgetValidator,
    WidgetLayoutEngine layoutEngine,
    GridPacker gridPacker,
    DashboardService dashboardService,
    UserService userService,
    AiPromptBuilder promptBuilder,
    TimeProvider timeProvider,
    ILogger<AiDashboardGenerationService> logger)
{
    public async Task<Result<AiGenerationResult>> GenerateAsync(
        Dashboard dashboard,
        string? promptOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveConfig = ResolveAiConfig(dashboard);
        if (effectiveConfig == null || effectiveConfig.ConnectionMode == AiConnectionMode.None)
        {
            return StoreError(dashboard, "AI is not configured. Set up an AI connection in Settings or the dashboard.");
        }

        var aiServiceResult = aiServiceFactory.Create(effectiveConfig, dashboard.Id.ToString());
        if (aiServiceResult.IsFailure)
        {
            return StoreError(dashboard, aiServiceResult.Error);
        }

        if (!string.IsNullOrWhiteSpace(promptOverride))
        {
            dashboard.AiPrompt = promptOverride;
        }

        var aiService = aiServiceResult.Value;
        var aiData = await dataFetcher.FetchAsync(dashboard.Id.ToString());
        var layoutConfig = dashboard.LayoutConfig ?? CreateDefaultLayoutConfig(dashboard);

        var (systemPrompt, userPrompt) = promptBuilder.BuildPrompt(
            dashboard,
            layoutConfig,
            aiData);

        var totalPromptChars = systemPrompt.Length + userPrompt.Length;
        var promptTokenEstimate = totalPromptChars / 4;

        logger.LogInformation(
            "Generating AI dashboard for {DashboardId} ({DashboardName}), prompt length: {SystemLen}+{UserLen} chars (~{Tokens} tokens)",
            dashboard.Id, dashboard.Name, systemPrompt.Length, userPrompt.Length, promptTokenEstimate);

        var completionResult = await aiService.GenerateCompletionAsync(systemPrompt, userPrompt, cancellationToken);
        if (completionResult.IsFailure)
        {
            return StoreError(dashboard, completionResult.Error);
        }

        var parseResult = responseParser.Parse(completionResult.Value);
        if (parseResult.IsFailure)
        {
            logger.LogWarning(
                "AI response parsing failed: {Error}. Running JSON repair pass. Raw response: {Response}",
                parseResult.Error, completionResult.Value);

            var repairResult = await responseParser.RepairAndParseAsync(
                aiService, completionResult.Value, parseResult.Error, cancellationToken);
            if (repairResult.IsFailure)
            {
                return StoreError(dashboard, $"AI returned invalid JSON and repair failed: {repairResult.Error}");
            }

            parseResult = repairResult;
        }

        var validatedWidgets = widgetValidator.ValidateAndRepair(parseResult.Value, aiData, dashboard);
        if (validatedWidgets.Count == 0)
        {
            return StoreError(dashboard, "All AI-generated widgets were invalid after validation");
        }

        var gridCols = layoutConfig.GridCols > 0 ? layoutConfig.GridCols : 12;
        var gridRows = layoutConfig.GridRows > 0 ? layoutConfig.GridRows : 8;

        layoutEngine.ComputeSizes(validatedWidgets, aiData, layoutConfig, gridCols);
        var placedWidgets = gridPacker.Pack(validatedWidgets, layoutConfig.Widgets, gridCols, gridRows);

        if (placedWidgets.Count == 0)
        {
            return StoreError(dashboard, "No widgets could be placed on the grid");
        }

        dashboard.AiGeneratedWidgets = placedWidgets;
        dashboard.LastAiGenerationTime = timeProvider.GetUtcNow();
        dashboard.LastAiGenerationError = null;
        dashboardService.UpdateDashboard(dashboard);

        logger.LogInformation(
            "AI generated {WidgetCount} widgets for dashboard {DashboardId} ({Placed} placed on grid)",
            validatedWidgets.Count, dashboard.Id, placedWidgets.Count);

        return new AiGenerationResult
        {
            Widgets = placedWidgets,
            DataSummary = new AiDataSummary
            {
                EntityStates = aiData.EntityStates.Count,
                TodoLists = [.. aiData.TodoItems.Keys],
                Calendars = [.. aiData.CalendarEvents.Keys],
                WeatherEntities = [.. aiData.WeatherForecasts.Keys],
                RssFeeds = [.. aiData.RssFeedEntries.Keys]
            },
            PromptTokenEstimate = promptTokenEstimate
        };
    }

    private Result<AiGenerationResult> StoreError(Dashboard dashboard, string error)
    {
        dashboard.LastAiGenerationError = error;
        dashboardService.UpdateDashboard(dashboard);
        return Result.Failure<AiGenerationResult>(error);
    }

    private static LayoutConfig CreateDefaultLayoutConfig(Dashboard dashboard)
    {
        var (width, height) = dashboard.GetEffectiveSize();
        return new LayoutConfig
        {
            Width = width,
            Height = height,
            GridCols = 12,
            GridRows = 8,
            ColorScheme = new ColorScheme
            {
                Name = "Default",
                Palette = new List<string> { "#000000", "#ffffff", "#ff0000" },
                Background = "#ffffff",
                CanvasBackgroundColor = "#ffffff",
                WidgetBackgroundColor = "#ffffff",
                WidgetBorderColor = "#000000",
                WidgetTitleTextColor = "#000000",
                WidgetTextColor = "#000000",
                IconColor = "#ff0000",
                Foreground = "#000000",
                Accent = "#ff0000",
                Text = "#000000"
            },
            Widgets = new List<WidgetConfig>(),
            CanvasPadding = 8,
            WidgetGap = 8,
            WidgetBorder = 1,
            WidgetPadding = 8,
            TitleFontSize = 14,
            TextFontSize = 12,
            TitleFontWeight = 700,
            TextFontWeight = 400
        };
    }

    private AiConfig? ResolveAiConfig(Dashboard dashboard)
    {
        if (dashboard.AiConfig != null && dashboard.AiConfig.ConnectionMode == AiConnectionMode.HomeAssistant)
        {
            return dashboard.AiConfig;
        }

        return userService.GetAiConfig(dashboard.UserId);
    }
}
