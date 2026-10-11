using System.Text.Json;

namespace EPaperDashboard.Models
{
    public class LayoutConfig
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int GridCols { get; set; }
        public int GridRows { get; set; }
        public ColorScheme ColorScheme { get; set; } = new();
        public List<WidgetConfig> Widgets { get; set; } = new();
        public int CanvasPadding { get; set; }
        public int WidgetGap { get; set; }
        public int WidgetBorder { get; set; }
        public int WidgetPadding { get; set; }
        public int TitleFontSize { get; set; }
        public int TextFontSize { get; set; }
        public int TitleFontWeight { get; set; }
        public int TextFontWeight { get; set; }
    }

    public class ColorScheme
    {
        public string Name { get; set; } = string.Empty;
        public string? Variant { get; set; }
        public List<string> Palette { get; set; } = new();
        public string Background { get; set; } = string.Empty;
        public string CanvasBackgroundColor { get; set; } = string.Empty;
        public string WidgetBackgroundColor { get; set; } = string.Empty;
        public string WidgetBorderColor { get; set; } = string.Empty;
        public string WidgetTitleTextColor { get; set; } = string.Empty;
        public string WidgetTextColor { get; set; } = string.Empty;
        public string IconColor { get; set; } = string.Empty;
        public string Foreground { get; set; } = string.Empty;
        public string Accent { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public class WidgetConfig
    {
        // A bare `JsonElement` defaults to ValueKind.Undefined, which System.Text.Json refuses to
        // serialize (throws InvalidOperationException) — that would take down the whole dashboard
        // render whenever a widget's Config was never explicitly set (e.g. a widget constructed
        // without one, or deserialized from a document predating this field). Defaulting to a real
        // empty object keeps Config always serializable. Clone() detaches the element from its
        // parsing JsonDocument so it stays valid independent of that document's lifetime.
        private static readonly JsonElement EmptyConfig = JsonDocument.Parse("{}").RootElement.Clone();

        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public WidgetPosition Position { get; set; } = new();
        public JsonElement Config { get; set; } = EmptyConfig;
        public WidgetColorOverrides? ColorOverrides { get; set; }
        public string? TitleOverride { get; set; }
        public bool ShowTitle { get; set; } = true;
        /// <summary>Designer-only layer name; not rendered.</summary>
        public string? Name { get; set; }
        /// <summary>Per-widget font sizes that replace the dashboard defaults when set.</summary>
        public WidgetFontOverrides? FontOverrides { get; set; }
    }

    public class WidgetFontOverrides
    {
        public int? TitleFontSize { get; set; }
        public int? TextFontSize { get; set; }
    }

    public class WidgetPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        /// <summary>Legacy derived value retained for persisted-layout compatibility.</summary>
        public double? PixelX { get; set; }
        /// <summary>Legacy derived value retained for persisted-layout compatibility.</summary>
        public double? PixelY { get; set; }
        /// <summary>Legacy derived value retained for persisted-layout compatibility.</summary>
        public double? PixelWidth { get; set; }
        /// <summary>Legacy derived value retained for persisted-layout compatibility.</summary>
        public double? PixelHeight { get; set; }
    }

    public class WidgetColorOverrides
    {
        public string? WidgetBackgroundColor { get; set; }
        public string? WidgetBorderColor { get; set; }
        public string? WidgetTitleTextColor { get; set; }
        public string? WidgetTextColor { get; set; }
        public string? IconColor { get; set; }
    }
}
