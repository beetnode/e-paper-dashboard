using System.Text.Json;
using EPaperDashboard.Models.Rendering;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Models.Rendering;

public class WidgetRenderContextTests
{
    private static LayoutConfig Layout() => new(
        Width: 800, Height: 480, GridCols: 12, GridRows: 8,
        ColorScheme: new ColorSchemeConfig("Default", null, ["#000000", "#ffffff"], "#ffffff", "#ffffff", "#ffffff",
            "#000000", "#000000", "#000000", "#000000", "#000000", "#000000", "#000000"),
        Widgets: [], CanvasPadding: 0, WidgetGap: 0, WidgetBorder: 0, WidgetPadding: 0,
        TitleFontSize: 16, TextFontSize: 14, TitleFontWeight: 700, TextFontWeight: 400);

    private static WidgetConfigEntry Widget(WidgetFontOverridesConfig? fonts) => new(
        "w1", "markdown", new WidgetPositionConfig(0, 0, 1, 1), JsonDocument.Parse("{}").RootElement, null,
        FontOverrides: fonts);

    [Fact]
    public void Create_WithoutOverrides_UsesDashboardSizes()
    {
        var context = WidgetRenderContext.Create(Widget(null), Layout());

        context.TitleFontSize.Should().Be(16);
        context.TextFontSize.Should().Be(14);
    }

    [Fact]
    public void Create_WithOverrides_UsesWidgetSizes()
    {
        var context = WidgetRenderContext.Create(Widget(new WidgetFontOverridesConfig(24, null)), Layout());

        context.TitleFontSize.Should().Be(24);
        context.TextFontSize.Should().Be(14);
    }
}
