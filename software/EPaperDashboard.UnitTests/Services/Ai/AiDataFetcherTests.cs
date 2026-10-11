using CSharpFunctionalExtensions;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Ai;
using EPaperDashboard.Services.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Ai;

public class AiDataFetcherTests
{
    private readonly Mock<IEntityStateProvider> _entityStateProvider = new();
    private readonly Mock<ITodoDataProvider> _todoDataProvider = new();
    private readonly Mock<ICalendarDataProvider> _calendarDataProvider = new();
    private readonly Mock<IWeatherForecastProvider> _weatherForecastProvider = new();
    private readonly Mock<IRssFeedDataProvider> _rssFeedDataProvider = new();

    private AiDataFetcher CreateSut()
    {
        _entityStateProvider
            .Setup(p => p.FetchAllEntityStatesAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<List<EntityState>, string>([]));
        _todoDataProvider
            .Setup(p => p.FetchAllTodoItemsAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<TodoItem>>, string>([]));
        _calendarDataProvider
            .Setup(p => p.FetchAllCalendarEventsAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<CalendarEvent>>, string>([]));
        _weatherForecastProvider
            .Setup(p => p.FetchAllWeatherForecastsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<WeatherForecast>>, string>([]));
        _rssFeedDataProvider
            .Setup(p => p.FetchAllRssFeedEntriesAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<RssFeedEntry>>, string>([]));

        return new AiDataFetcher(
            _entityStateProvider.Object,
            _todoDataProvider.Object,
            _calendarDataProvider.Object,
            _weatherForecastProvider.Object,
            _rssFeedDataProvider.Object,
            NullLogger<AiDataFetcher>.Instance);
    }

    [Fact]
    public async Task FetchAsync_AllProvidersSucceed_PopulatesSnapshotFromAllSources()
    {
        var sut = CreateSut();
        _entityStateProvider
            .Setup(p => p.FetchAllEntityStatesAsync("dash1"))
            .ReturnsAsync(Result.Success<List<EntityState>, string>([new EntityState { EntityId = "sensor.x" }]));

        var result = await sut.FetchAsync("dash1");

        result.EntityStates.Should().ContainKey("sensor.x");
    }

    [Fact]
    public async Task FetchAsync_OneProviderFails_OthersStillPopulateSnapshot()
    {
        var sut = CreateSut();
        _entityStateProvider
            .Setup(p => p.FetchAllEntityStatesAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<List<EntityState>, string>("boom"));
        _todoDataProvider
            .Setup(p => p.FetchAllTodoItemsAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<TodoItem>>, string>(
                new Dictionary<string, List<TodoItem>> { ["todo.list"] = [] }));

        var result = await sut.FetchAsync("dash1");

        result.EntityStates.Should().BeEmpty();
        result.TodoItems.Should().ContainKey("todo.list");
    }

    [Fact]
    public async Task FetchAsync_ProviderThrows_IsTreatedLikeAFailureNotAnException()
    {
        var sut = CreateSut();
        _entityStateProvider
            .Setup(p => p.FetchAllEntityStatesAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("network error"));

        var act = async () => await sut.FetchAsync("dash1");

        await act.Should().NotThrowAsync();
        var result = await sut.FetchAsync("dash1");
        result.EntityStates.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_AllProvidersFail_ReturnsEmptySnapshotWithoutThrowing()
    {
        var sut = CreateSut();
        _entityStateProvider.Setup(p => p.FetchAllEntityStatesAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<List<EntityState>, string>("x"));
        _todoDataProvider.Setup(p => p.FetchAllTodoItemsAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<Dictionary<string, List<TodoItem>>, string>("x"));
        _calendarDataProvider.Setup(p => p.FetchAllCalendarEventsAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Result.Failure<Dictionary<string, List<CalendarEvent>>, string>("x"));
        _weatherForecastProvider.Setup(p => p.FetchAllWeatherForecastsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<Dictionary<string, List<WeatherForecast>>, string>("x"));
        _rssFeedDataProvider.Setup(p => p.FetchAllRssFeedEntriesAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<Dictionary<string, List<RssFeedEntry>>, string>("x"));

        var result = await sut.FetchAsync("dash1");

        result.EntityStates.Should().BeEmpty();
        result.TodoItems.Should().BeEmpty();
        result.CalendarEvents.Should().BeEmpty();
        result.WeatherForecasts.Should().BeEmpty();
        result.RssFeedEntries.Should().BeEmpty();
    }
}
