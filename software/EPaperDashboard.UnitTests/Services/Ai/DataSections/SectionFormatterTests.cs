using System.Text.Json;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Ai;
using EPaperDashboard.Services.Ai.DataSections;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Ai.DataSections;

public class EntityStateSectionFormatterTests
{
    private static readonly EntityStateSectionFormatter Sut = new();

    [Fact]
    public void HasData_NoEntityStates_ReturnsFalse() =>
        Sut.HasData(new AiDataSnapshot()).Should().BeFalse();

    [Fact]
    public void HasData_HasEntityStates_ReturnsTrue()
    {
        var data = new AiDataSnapshot { EntityStates = { ["sensor.x"] = new EntityState() } };

        Sut.HasData(data).Should().BeTrue();
    }

    [Fact]
    public void FormatSection_EntityWithFriendlyNameAndUnit_IncludesBoth()
    {
        var data = new AiDataSnapshot
        {
            EntityStates =
            {
                ["sensor.temp"] = new EntityState
                {
                    EntityId = "sensor.temp",
                    State = "21.5",
                    Attributes = { ["friendly_name"] = "Living Room Temp", ["unit_of_measurement"] = "°C" }
                }
            }
        };

        var result = Sut.FormatSection(data);

        result.Should().Contain("Living Room Temp (sensor.temp): 21.5 °C");
    }

    [Fact]
    public void FormatSection_EntityWithoutFriendlyNameOrUnit_FallsBackToEntityIdAndRawState()
    {
        var data = new AiDataSnapshot
        {
            EntityStates = { ["sensor.raw"] = new EntityState { EntityId = "sensor.raw", State = "on" } }
        };

        var result = Sut.FormatSection(data);

        result.Should().Contain("- sensor.raw: on");
    }
}

public class TodoItemsSectionFormatterTests
{
    private static readonly TodoItemsSectionFormatter Sut = new();

    [Fact]
    public void HasData_NoTodoLists_ReturnsFalse() =>
        Sut.HasData(new AiDataSnapshot()).Should().BeFalse();

    [Fact]
    public void FormatSection_ListsItemsWithStatusAndSummary_LimitsToTenPerList()
    {
        var items = Enumerable.Range(0, 15)
            .Select(i => new TodoItem { Summary = $"Task {i}", Status = "needs_action" })
            .ToList();
        var data = new AiDataSnapshot { TodoItems = { ["todo.list"] = items } };

        var result = Sut.FormatSection(data);

        result.Should().Contain("Todo: todo.list (15 items)");
        result.Should().Contain("[needs_action] Task 0");
        result.Should().NotContain("Task 10");
    }
}

public class CalendarEventsSectionFormatterTests
{
    private static readonly CalendarEventsSectionFormatter Sut = new();

    [Fact]
    public void HasData_NoEvents_ReturnsFalse() =>
        Sut.HasData(new AiDataSnapshot()).Should().BeFalse();

    [Fact]
    public void FormatSection_AllDayEvent_ShowsAllDayInsteadOfStartTime()
    {
        var data = new AiDataSnapshot
        {
            CalendarEvents = { ["calendar.a"] = [new CalendarEvent { Summary = "Birthday", AllDay = true, Start = "2026-03-17T00:00:00" }] }
        };

        var result = Sut.FormatSection(data);

        result.Should().Contain("All day: Birthday");
    }

    [Fact]
    public void FormatSection_TimedEvent_ShowsStartTime()
    {
        var data = new AiDataSnapshot
        {
            CalendarEvents = { ["calendar.a"] = [new CalendarEvent { Summary = "Meeting", AllDay = false, Start = "2026-03-17T09:00:00" }] }
        };

        var result = Sut.FormatSection(data);

        result.Should().Contain("2026-03-17T09:00:00: Meeting");
    }

    [Fact]
    public void FormatSection_MoreThanTenEvents_LimitsToTen()
    {
        var events = Enumerable.Range(0, 12).Select(i => new CalendarEvent { Summary = $"Event {i}" }).ToList();
        var data = new AiDataSnapshot { CalendarEvents = { ["calendar.a"] = events } };

        var result = Sut.FormatSection(data);

        result.Should().Contain("Event 9");
        result.Should().NotContain("Event 10");
    }
}

public class RssFeedSectionFormatterTests
{
    private static readonly RssFeedSectionFormatter Sut = new();

    [Fact]
    public void HasData_NoEntries_ReturnsFalse() =>
        Sut.HasData(new AiDataSnapshot()).Should().BeFalse();

    [Fact]
    public void FormatSection_MoreThanFiveEntries_LimitsToFive()
    {
        var entries = Enumerable.Range(0, 8).Select(i => new RssFeedEntry { Title = $"Headline {i}" }).ToList();
        var data = new AiDataSnapshot { RssFeedEntries = { ["sensor.feed"] = entries } };

        var result = Sut.FormatSection(data);

        result.Should().Contain("Headline 4");
        result.Should().NotContain("Headline 5");
    }
}

public class WeatherForecastSectionFormatterTests
{
    private static readonly WeatherForecastSectionFormatter Sut = new();

    [Fact]
    public void HasData_NoForecasts_ReturnsFalse() =>
        Sut.HasData(new AiDataSnapshot()).Should().BeFalse();

    [Fact]
    public void FormatSection_ForecastEntryWithFields_IncludesAllPresentFields()
    {
        var entry = new WeatherForecast
        {
            Datetime = "2026-03-18",
            Condition = "sunny",
            Temperature = 22,
            TempLow = 12,
            PrecipitationProbability = 5,
            WindSpeed = 10
        };
        var data = new AiDataSnapshot
        {
            WeatherForecasts = { ["weather.home"] = [entry] }
        };

        var result = Sut.FormatSection(data);

        result.Should().Contain("2026-03-18").And.Contain("sunny").And.Contain("22°").And.Contain("low 12°").And.Contain("5% precip").And.Contain("wind 10");
    }

    [Fact]
    public void FormatSection_EntryWithMissingFields_IsHandledWithoutThrowing()
    {
        var data = new AiDataSnapshot
        {
            WeatherForecasts = { ["weather.home"] = [new WeatherForecast()] }
        };

        var act = () => Sut.FormatSection(data);

        act.Should().NotThrow();
    }
}
