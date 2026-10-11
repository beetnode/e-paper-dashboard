using System.Text.Json;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Rendering;
using FluentAssertions;
using SixLabors.ImageSharp;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Rendering;

public class RenderingUtilitiesTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void GetStringProp_PropertyIsString_ReturnsValue() =>
        RenderingUtilities.GetStringProp(Json(new { name = "x" }), "name").Should().Be("x");

    [Fact]
    public void GetStringProp_PropertyIsNotString_ReturnsNull() =>
        RenderingUtilities.GetStringProp(Json(new { name = 1 }), "name").Should().BeNull();

    [Fact]
    public void GetIntProp_PropertyIsNumber_ReturnsValue() =>
        RenderingUtilities.GetIntProp(Json(new { n = 5 }), "n").Should().Be(5);

    [Fact]
    public void GetDoubleProp_PropertyIsNumber_ReturnsValue() =>
        RenderingUtilities.GetDoubleProp(Json(new { n = 5.5 }), "n").Should().Be(5.5);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void GetBoolProp_PropertyIsBoolean_ReturnsValue(bool value, bool expected) =>
        RenderingUtilities.GetBoolProp(Json(new { b = value }), "b").Should().Be(expected);

    [Fact]
    public void GetBoolProp_PropertyMissing_ReturnsNull() =>
        RenderingUtilities.GetBoolProp(Json(new { }), "b").Should().BeNull();

    [Fact]
    public void GetStringArrayProp_ArrayOfStrings_ReturnsThem() =>
        RenderingUtilities.GetStringArrayProp(Json(new { tags = new[] { "a", "b" } }), "tags")
            .Should().Equal("a", "b");

    [Fact]
    public void GetStringArrayProp_NotAnArray_ReturnsNull() =>
        RenderingUtilities.GetStringArrayProp(Json(new { tags = "a" }), "tags").Should().BeNull();

    [Fact]
    public void GetStringArrayProp_MixedArrayFiltersNonStringElements() =>
        RenderingUtilities.GetStringArrayProp(Json(new { tags = new object[] { "a", 1, "b" } }), "tags")
            .Should().Equal("a", "b");

    [Fact]
    public void GetBadgeDoubleProp_PropertyIsNumber_ReturnsValue() =>
        RenderingUtilities.GetBadgeDoubleProp(Json(new { value = 3.5 }), "value").Should().Be(3.5);

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void GetEntityAttr_KnownValueTypes_FormatsAsString(object value, string expected)
    {
        var state = new EntityState { Attributes = { ["key"] = value } };

        RenderingUtilities.GetEntityAttr(state, "key").Should().Be(expected);
    }

    [Fact]
    public void GetEntityAttr_MissingKey_ReturnsNull()
    {
        var state = new EntityState();

        RenderingUtilities.GetEntityAttr(state, "missing").Should().BeNull();
    }

    [Fact]
    public void ResolvePixelPosition_StaleExplicitPixelValuesProvided_ComputesFromGrid()
    {
        var pos = new WidgetPositionConfig(1, 0, 2, 1, PixelX: 10, PixelY: 20, PixelWidth: 30, PixelHeight: 40);
        var layout = new LayoutConfig(100, 100, 4, 4, null!, [], 0, 0, 0, 0, 0, 0, 0, 0);

        var (x, y, w, h) = RenderingUtilities.ResolvePixelPosition(pos, layout);

        x.Should().Be(25);
        y.Should().Be(0);
        w.Should().Be(50);
        h.Should().Be(25);
    }

    [Fact]
    public void ResolvePixelPosition_NoExplicitPixelValues_ComputesFromGridCell()
    {
        // 200x100 canvas, no padding/gap, 4x2 grid → each cell is 50x50.
        var pos = new WidgetPositionConfig(1, 0, 2, 1);
        var layout = new LayoutConfig(200, 100, 4, 2, null!, [], 0, 0, 0, 0, 0, 0, 0, 0);

        var (x, y, w, h) = RenderingUtilities.ResolvePixelPosition(pos, layout);

        x.Should().Be(50);
        y.Should().Be(0);
        w.Should().Be(100);
        h.Should().Be(50);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("not-a-date", "not-a-date")]
    public void FormatEventDate_NullEmptyOrUnparseable_HandlesGracefully(string? input, string expected) =>
        RenderingUtilities.FormatEventDate(input).Should().Be(expected);

    [Fact]
    public void FormatEventDate_DateOnlyString_FormatsAsDayOfWeekAndDate() =>
        RenderingUtilities.FormatEventDate("2026-03-17").Should().Be("Tue, Mar 17");

    [Fact]
    public void FormatEventDate_DateTimeString_FormatsWithTime() =>
        RenderingUtilities.FormatEventDate("2026-03-17T09:30:00Z").Should().Contain("Mar 17").And.Contain("09:30");

    [Fact]
    public void FormatForecastTime_HourlyMode_FormatsAsTime() =>
        RenderingUtilities.FormatForecastTime("2026-03-17T09:30:00Z", "hourly").Should().Be("09:30");

    [Fact]
    public void FormatForecastTime_WeeklyMode_FormatsAsDayName() =>
        RenderingUtilities.FormatForecastTime("2026-03-17T09:30:00Z", "weekly").Should().Be("Tue");

    [Fact]
    public void FormatForecastTime_DefaultMode_FormatsAsDayNumber() =>
        RenderingUtilities.FormatForecastTime("2026-03-17T09:30:00Z", "daily").Should().Be("17");

    [Fact]
    public void FormatForecastTime_NullOrEmpty_ReturnsEmpty() =>
        RenderingUtilities.FormatForecastTime(null, "hourly").Should().Be("");

    [Theory]
    [InlineData("sunny", "Sunny")]
    [InlineData("partlycloudy", "Pt. Cloudy")]
    [InlineData("lightning-rainy", "Stormy")]
    [InlineData("SUNNY", "Sunny")]
    [InlineData("some-unknown-condition", "some-unknown-condition")]
    public void FormatCondition_MapsKnownConditionsCaseInsensitively(string input, string expected) =>
        RenderingUtilities.FormatCondition(input).Should().Be(expected);

    [Fact]
    public void FormatCondition_NullOrEmpty_ReturnsEmpty() =>
        RenderingUtilities.FormatCondition(null).Should().Be("");

    [Theory]
    [InlineData(null, "")]
    public void RoundNum_Null_ReturnsEmpty(object? value, string expected) =>
        RenderingUtilities.RoundNum(value).Should().Be(expected);

    [Fact]
    public void RoundNum_Double_RoundsToWholeNumber() =>
        RenderingUtilities.RoundNum(3.7).Should().Be("4");

    [Fact]
    public void RoundNum_Long_ReturnsAsIs() =>
        RenderingUtilities.RoundNum(42L).Should().Be("42");

    [Fact]
    public void RoundNum_NumericString_ParsesAndRounds() =>
        RenderingUtilities.RoundNum("3.2").Should().Be("3");

    [Fact]
    public void RoundNum_NonNumericValue_ReturnsToString() =>
        RenderingUtilities.RoundNum("not-a-number").Should().Be("not-a-number");

    [Fact]
    public void RoundNumOneDecimal_Double_RoundsToOneDecimalPlace() =>
        RenderingUtilities.RoundNumOneDecimal(3.14159).Should().Be("3.1");

    [Fact]
    public void RoundNumOneDecimal_Null_ReturnsEmpty() =>
        RenderingUtilities.RoundNumOneDecimal(null).Should().Be("");

    [Fact]
    public void BuildSmoothedPath_TwoPoints_BoundsSpanBothPoints()
    {
        var points = new[] { new PointF(0, 0), new PointF(10, 10) };

        var path = RenderingUtilities.BuildSmoothedPath(points, tension: 0.5f);

        path.Bounds.Left.Should().BeLessThanOrEqualTo(0);
        path.Bounds.Right.Should().BeGreaterThanOrEqualTo(10);
    }
}
