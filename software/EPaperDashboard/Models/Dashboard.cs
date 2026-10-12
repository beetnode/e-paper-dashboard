using System.Text.Json.Serialization;

namespace EPaperDashboard.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RenderingMode
    {
        Custom = 0,
        HomeAssistant = 1
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DashboardOrientation
    {
        Landscape = 0,
        Portrait = 1
    }

    public class Dashboard
    {
        public DashboardId Id { get; set; } = DashboardId.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public UserId UserId { get; set; } = UserId.Empty;
        /// <summary>Retired Home Assistant token; read only to move it into a connection at startup.</summary>
        public string? AccessToken { get; set; }
        /// <summary>Retired Home Assistant server URL; read only to move it into a connection at startup.</summary>
        public string? Host { get; set; }
        public string? Path { get; set; }
        /// <summary>Named Home Assistant connection used by "Home Assistant Dashboard" rendering and notifications.</summary>
        public string? HomeAssistantConnectionId { get; set; }
        public List<TimeOnly>? UpdateTimes { get; set; }
        public LayoutConfig? LayoutConfig { get; set; }
        public DateTimeOffset? LastUpdateTime { get; set; }
        public RenderingMode RenderingMode { get; set; } = RenderingMode.Custom;
        public DashboardOrientation Orientation { get; set; } = DashboardOrientation.Landscape;

        public int ScreenWidth { get; set; } = DashboardSizePreset.Default.Width;

        public int ScreenHeight { get; set; } = DashboardSizePreset.Default.Height;

        // AI dashboard generation
        public AiConfig? AiConfig { get; set; }
        public bool IsAiEnabled { get; set; }
        public string? AiPrompt { get; set; }
        public int AiLeadTimeMinutes { get; set; } = 5;
        public List<WidgetConfig>? AiGeneratedWidgets { get; set; }
        public DateTimeOffset? LastAiGenerationTime { get; set; }
        public string? LastAiGenerationError { get; set; }

        // AI content widget cache (widgetId → generated content)
        public Dictionary<string, string>? AiContentCache { get; set; }
        public DateTimeOffset? LastAiContentCacheTime { get; set; }

        public (int Width, int Height) GetEffectiveSize()
        {
            var w = ScreenWidth > 0 ? ScreenWidth : DashboardSizePreset.Default.Width;
            var h = ScreenHeight > 0 ? ScreenHeight : DashboardSizePreset.Default.Height;
            return Orientation == DashboardOrientation.Portrait
                ? (h, w)
                : (w, h);
        }

        /// <summary>
        /// Returns the layout config with AI-generated widgets merged in (if enabled).
        /// </summary>
        public LayoutConfig GetMergedLayoutConfig()
            => GetMergedLayoutConfig(LayoutConfig!);

        /// <summary>
        /// Returns the supplied layout with generated widgets merged in. This overload is used
        /// for transient designer previews so unsaved layout changes follow the production path.
        /// </summary>
        public LayoutConfig GetMergedLayoutConfig(LayoutConfig layout)
        {
            if (!IsAiEnabled || AiGeneratedWidgets is not { Count: > 0 })
                return layout;

            return new LayoutConfig
            {
                Width = layout.Width,
                Height = layout.Height,
                GridCols = layout.GridCols,
                GridRows = layout.GridRows,
                ColorScheme = layout.ColorScheme,
                Widgets = new List<WidgetConfig>(layout.Widgets).Concat(AiGeneratedWidgets).ToList(),
                CanvasPadding = layout.CanvasPadding,
                WidgetGap = layout.WidgetGap,
                WidgetBorder = layout.WidgetBorder,
                WidgetPadding = layout.WidgetPadding,
                TitleFontSize = layout.TitleFontSize,
                TextFontSize = layout.TextFontSize,
                TitleFontWeight = layout.TitleFontWeight,
                TextFontWeight = layout.TextFontWeight
            };
        }
    }
}
