using System.Text.Json;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Rendering;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Rendering;

public class TransientLayoutValidatorTests
{
    private static LayoutConfig ValidLayout() => new()
    {
        Width = 800,
        Height = 480,
        GridCols = 12,
        GridRows = 8,
        Widgets =
        [
            new WidgetConfig
            {
                Id = "header-1",
                Type = "header",
                Position = new WidgetPosition { X = 0, Y = 0, W = 12, H = 1 },
                Config = JsonSerializer.SerializeToElement(new { title = "Dashboard" })
            }
        ]
    };

    [Fact]
    public void Validate_ValidLayout_ReturnsNoError()
        => TransientLayoutValidator.Validate(ValidLayout()).Should().BeNull();

    [Fact]
    public void Validate_WidgetOutsideGrid_ReturnsError()
    {
        var layout = ValidLayout();
        layout.Widgets[0].Position.X = 1;

        TransientLayoutValidator.Validate(layout).Should().Contain("outside the dashboard grid");
    }

    [Fact]
    public void Validate_EditableElementOutsideContent_ReturnsError()
    {
        var layout = ValidLayout();
        layout.Widgets[0].Config = JsonSerializer.SerializeToElement(new
        {
            badges = new[] { new { x = 90, y = 0, w = 20, h = 20 } }
        });

        TransientLayoutValidator.Validate(layout).Should().Contain("content area");
    }

    [Fact]
    public void Validate_FontOverrideOutOfRange_ReturnsError()
    {
        var layout = ValidLayout();
        layout.Widgets[0].FontOverrides = new WidgetFontOverrides { TextFontSize = 0 };

        TransientLayoutValidator.Validate(layout).Should().Contain("font sizes");
    }

    [Fact]
    public void Validate_FontOverrideWithinRange_ReturnsNoError()
    {
        var layout = ValidLayout();
        layout.Widgets[0].FontOverrides = new WidgetFontOverrides { TitleFontSize = 24 };

        TransientLayoutValidator.Validate(layout).Should().BeNull();
    }
}
