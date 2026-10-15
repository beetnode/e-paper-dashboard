using System.Text.Json;
using EPaperDashboard.Utilities;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Providers;

public class SsrDataProviderTests
{
    private readonly Mock<IEntityStateProvider> _entityStateProvider = new();
    private readonly Mock<ITodoDataProvider> _todoDataProvider = new();
    private readonly Mock<ICalendarDataProvider> _calendarDataProvider = new();
    private readonly Mock<IWeatherForecastProvider> _weatherForecastProvider = new();
    private readonly Mock<IRssFeedDataProvider> _rssFeedDataProvider = new();
    private readonly Mock<IEntityHistoryProvider> _entityHistoryProvider = new();
    private readonly Mock<IAiContentProvider> _aiContentProvider = new();

    private SsrDataProvider CreateSut()
    {
        _entityStateProvider
            .Setup(p => p.FetchEntityStatesAsync(It.IsAny<string>(), It.IsAny<string[]>()))
            .ReturnsAsync(Result.Success<List<EntityState>>(new List<EntityState>()));

        return new SsrDataProvider(
            _entityStateProvider.Object,
            _todoDataProvider.Object,
            _calendarDataProvider.Object,
            _weatherForecastProvider.Object,
            _entityHistoryProvider.Object,
            _aiContentProvider.Object,
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            NullLogger<SsrDataProvider>.Instance, _rssFeedDataProvider.Object);
    }

    private static WidgetConfigEntry Widget(string type, object config, string id = "w1") => new(
        id, type, new WidgetPositionConfig(0, 0, 1, 1), JsonSerializer.SerializeToElement(config), null);

    private static LayoutConfig LayoutWith(params WidgetConfigEntry[] widgets) =>
        new(100, 100, 4, 4, null!, [.. widgets], 0, 0, 0, 0, 0, 0, 0, 0);

    [Fact]
    public async Task FailedConnectionDoesNotDiscardAnotherConnectionsStates()
    {
        var first = EPaperDashboard.Services.Plugins.SourceReference.Create(Guid.NewGuid().ToString("N"), "weather.home");
        var second = EPaperDashboard.Services.Plugins.SourceReference.Create(Guid.NewGuid().ToString("N"), "weather.home");
        var sut = CreateSut();
        _entityStateProvider.Setup(p => p.FetchEntityStatesAsync("dash1", It.Is<string[]>(ids => ids.Length == 1 && ids[0] == first), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<List<EntityState>>("Offline"));
        _entityStateProvider.Setup(p => p.FetchEntityStatesAsync("dash1", It.Is<string[]>(ids => ids.Length == 1 && ids[0] == second), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<List<EntityState>>([new() { EntityId = second, State = "sunny" }]));
        var data = await sut.FetchSsrDataAsync("dash1", LayoutWith(Widget("weather", new { entityId = first }, "a"), Widget("weather", new { entityId = second }, "b")));
        data.EntityStates.Should().ContainKey(second);
        data.SourceStatuses[DataSourceKeys.Entity(first)].State.Should().Be("error");
        data.SourceStatuses[DataSourceKeys.Entity(second)].State.Should().Be("ready");
    }

    [Fact]
    public void PluginWidgetTemplatesUseTheSharedDataPlan()
    {
        var plugin = new Mock<EPaperDashboard.Services.Plugins.IDataProviderPlugin>();
        plugin.SetupGet(p => p.Manifest).Returns(new EPaperDashboard.Services.Plugins.ProviderManifest("google", "Google", [], Widgets: [new("google.calendar", "calendar"), new("google.tasks", "todo")]));
        var registry = new EPaperDashboard.Services.Plugins.ProviderRegistry([plugin.Object]);
        var plan = WidgetDataPlan.Create(LayoutWith(Widget("google.calendar", new { entityId = "calendar-source" }), Widget("google.tasks", new { entityId = "task-source" })), registry.ResolveWidgetType);
        plan.CalendarEntityIds.Should().ContainSingle().Which.Should().Be("calendar-source");
        plan.TodoEntityIds.Should().ContainSingle().Which.Should().Be("task-source");
    }

    [Fact]
    public async Task FetchSsrDataAsync_NoWidgets_ReturnsEmptyData()
    {
        var sut = CreateSut();

        var result = await sut.FetchSsrDataAsync("dash1", LayoutWith());

        result.TodoItems.Should().BeEmpty();
        result.CalendarEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchSsrDataAsync_TodoWidgetWithEntityId_PopulatesTodoItems()
    {
        _todoDataProvider
            .Setup(p => p.FetchTodoItemsAsync("dash1", "todo.list"))
            .ReturnsAsync(Result.Success<List<TodoItem>>([new TodoItem { Summary = "Buy milk" }]));
        var sut = CreateSut();
        var layout = LayoutWith(Widget("todo", new { entityId = "todo.list" }));

        var result = await sut.FetchSsrDataAsync("dash1", layout);

        result.TodoItems.Should().ContainKey("todo.list");
        result.TodoItems["todo.list"].Should().ContainSingle(t => t.Summary == "Buy milk");
    }

    [Fact]
    public async Task FetchSsrDataAsync_CalendarWidget_PopulatesCalendarEvents()
    {
        _calendarDataProvider
            .Setup(p => p.FetchCalendarEventsAsync("dash1", "calendar.a", 168))
            .ReturnsAsync(Result.Success<List<CalendarEvent>>([new CalendarEvent { Summary = "Meeting" }]));
        var sut = CreateSut();
        var layout = LayoutWith(Widget("calendar", new { entityId = "calendar.a" }));

        var result = await sut.FetchSsrDataAsync("dash1", layout);

        result.CalendarEvents.Should().ContainKey("calendar.a");
    }

    [Fact]
    public async Task FetchSsrDataAsync_WeatherForecastWidget_PopulatesForecastListFromResultDictionary()
    {
        var forecastList = new List<WeatherForecast> { new() { Condition = "sunny" } };
        _weatherForecastProvider
            .Setup(p => p.FetchWeatherForecastAsync("dash1", "weather.home", "daily"))
            .ReturnsAsync(Result.Success<List<WeatherForecast>>(forecastList));
        var sut = CreateSut();
        var layout = LayoutWith(Widget("weather-forecast", new { entityId = "weather.home" }));

        var result = await sut.FetchSsrDataAsync("dash1", layout);

        var key = WeatherForecastDataKey.Create("weather.home", "daily");
        result.WeatherForecasts.Should().ContainKey(key);
        result.WeatherForecasts[key].Should().BeSameAs(forecastList);
    }

    [Fact]
    public async Task FetchSsrDataAsync_WeatherForecastWidgetHourlyMode_RequestsHourlyForecastType()
    {
        _weatherForecastProvider
            .Setup(p => p.FetchWeatherForecastAsync("dash1", "weather.home", "hourly"))
            .ReturnsAsync(Result.Success<List<WeatherForecast>>([]));
        var sut = CreateSut();
        var layout = LayoutWith(Widget("weather-forecast", new { entityId = "weather.home", forecastMode = "hourly" }));

        await sut.FetchSsrDataAsync("dash1", layout);

        _weatherForecastProvider.Verify(p => p.FetchWeatherForecastAsync("dash1", "weather.home", "hourly"), Times.Once);
    }

    [Fact]
    public async Task FetchSsrDataAsync_SameWeatherEntityWithDifferentModes_KeepsBothForecasts()
    {
        var daily = new List<WeatherForecast> { new() { Condition = "daily" } };
        var hourly = new List<WeatherForecast> { new() { Condition = "hourly" } };
        _weatherForecastProvider
            .Setup(p => p.FetchWeatherForecastAsync("dash1", "weather.home", "daily"))
            .ReturnsAsync(Result.Success<List<WeatherForecast>>(daily));
        _weatherForecastProvider
            .Setup(p => p.FetchWeatherForecastAsync("dash1", "weather.home", "hourly"))
            .ReturnsAsync(Result.Success<List<WeatherForecast>>(hourly));
        var sut = CreateSut();
        var layout = LayoutWith(
            Widget("weather-forecast", new { entityId = "weather.home", forecastMode = "daily" }, "daily"),
            Widget("weather-forecast", new { entityId = "weather.home", forecastMode = "hourly" }, "hourly"));

        var result = await sut.FetchSsrDataAsync("dash1", layout);

        result.WeatherForecasts[WeatherForecastDataKey.Create("weather.home", "daily")]
            .Should().BeSameAs(daily);
        result.WeatherForecasts[WeatherForecastDataKey.Create("weather.home", "hourly")]
            .Should().BeSameAs(hourly);
    }

    [Fact]
    public async Task FetchSsrDataAsync_DuplicateTodoEntity_FetchesOnce()
    {
        _todoDataProvider
            .Setup(p => p.FetchTodoItemsAsync("dash1", "todo.list"))
            .ReturnsAsync(Result.Success<List<TodoItem>>([]));
        var sut = CreateSut();
        var layout = LayoutWith(
            Widget("todo", new { entityId = "todo.list" }, "todo-1"),
            Widget("todo", new { entityId = "todo.list" }, "todo-2"));

        await sut.FetchSsrDataAsync("dash1", layout);

        _todoDataProvider.Verify(p => p.FetchTodoItemsAsync("dash1", "todo.list"), Times.Once);
    }

    [Fact]
    public async Task FetchSsrDataAsync_RssFeedWidget_PopulatesRssEntries()
    {
        var sut = CreateSut();
        _rssFeedDataProvider.Setup(p => p.FetchRssFeedEntriesAsync("dash1", "sensor.feed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<List<RssFeedEntry>>([new() { Title = "Headline", Link = "https://example.com/item" }]));
        var layout = LayoutWith(Widget("rss-feed", new { entityId = "sensor.feed" }));

        var result = await sut.FetchSsrDataAsync("dash1", layout);

        result.RssFeedEntries.Should().ContainKey("sensor.feed");
        result.RssFeedEntries["sensor.feed"].Should().ContainSingle(item => item.Title == "Headline");
        _rssFeedDataProvider.Verify(
            provider => provider.FetchRssFeedEntriesAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    [Theory]
    [InlineData("1h", 1)]
    [InlineData("6h", 6)]
    [InlineData("24h", 24)]
    [InlineData("7d", 168)]
    [InlineData("30d", 720)]
    [InlineData("unknown", 24)]
    public async Task FetchSsrDataAsync_GraphWidget_TranslatesPeriodToHours(string period, int expectedHours)
    {
        _entityHistoryProvider
            .Setup(p => p.FetchEntityHistoryAsync("dash1", It.IsAny<IEnumerable<string>>(), expectedHours))
            .ReturnsAsync(Result.Success<Dictionary<string, List<HistoryState>>>(
                new Dictionary<string, List<HistoryState>> { ["sensor.temp"] = [] }));
        var sut = CreateSut();
        var layout = LayoutWith(Widget("graph", new
        {
            series = new[] { new { entityId = "sensor.temp" } },
            period
        }));

        var result = await sut.FetchSsrDataAsync("dash1", layout);

        result.HistoryData.Should().ContainKey("sensor.temp");
    }

    [Fact]
    public async Task FetchSsrDataAsync_SameGraphEntityWithDifferentPeriods_FetchesLongestPeriodOnce()
    {
        _entityHistoryProvider
            .Setup(p => p.FetchEntityHistoryAsync("dash1", It.IsAny<IEnumerable<string>>(), 720))
            .ReturnsAsync(Result.Success<Dictionary<string, List<HistoryState>>>(
                new Dictionary<string, List<HistoryState>> { ["sensor.temp"] = [] }));
        var sut = CreateSut();
        var layout = LayoutWith(
            Widget("graph", new { series = new[] { new { entityId = "sensor.temp" } }, period = "1h" }, "graph-1"),
            Widget("graph", new { series = new[] { new { entityId = "sensor.temp" } }, period = "30d" }, "graph-2"));

        await sut.FetchSsrDataAsync("dash1", layout);

        _entityHistoryProvider.Verify(
            p => p.FetchEntityHistoryAsync(
                "dash1",
                It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "sensor.temp" })),
                720),
            Times.Once);
        _entityHistoryProvider.Verify(
            p => p.FetchEntityHistoryAsync("dash1", It.IsAny<IEnumerable<string>>(), It.Is<int>(hours => hours != 720)),
            Times.Never);
    }

    [Fact]
    public async Task FetchSsrDataAsync_AiContentWidgetWithCachedContent_UsesCacheWithoutGenerating()
    {
        _aiContentProvider.Setup(p => p.GetCachedContent("dash1", "w1")).Returns("cached text");
        var sut = CreateSut();
        var layout = LayoutWith(Widget("ai-content", new { prompt = "summarize" }));

        var result = await sut.FetchSsrDataAsync("dash1", layout);

        result.AiContent.Should().ContainKey("w1").WhoseValue.Should().Be("cached text");
        _aiContentProvider.Verify(
            p => p.GenerateAndCacheContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FetchSsrDataAsync_AiContentWidgetNoCache_DoesNotGenerateDuringRender()
    {
        _aiContentProvider.Setup(p => p.GetCachedContent("dash1", "w1")).Returns((string?)null);
        var sut = CreateSut();
        var layout = LayoutWith(Widget("ai-content", new { prompt = "summarize" }));

        var result = await sut.FetchSsrDataAsync("dash1", layout);

        result.AiContent.Should().NotContainKey("w1");
        _aiContentProvider.Verify(
            p => p.GenerateAndCacheContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FetchSsrDataAsync_HeaderWidgetWithBadgeEntityIds_IncludesThemInEntityStatesFetch()
    {
        var sut = CreateSut();
        var layout = LayoutWith(Widget("header", new
        {
            title = "Home",
            badges = new[] { new { entityId = "sensor.temp" } }
        }));

        await sut.FetchSsrDataAsync("dash1", layout);

        _entityStateProvider.Verify(
            p => p.FetchEntityStatesAsync("dash1", It.Is<string[]>(ids => ids.Contains("sensor.temp"))),
            Times.Once);
    }

    [Fact]
    public async Task FetchSsrDataAsync_ReusesSuccessfulSourceDataUntilBypassed()
    {
        _todoDataProvider
            .Setup(p => p.FetchTodoItemsAsync("dash1", "todo.list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<List<TodoItem>>([new TodoItem { Summary = "Cached" }]));
        var sut = CreateSut();
        var layout = LayoutWith(Widget("todo", new { entityId = "todo.list" }));

        await sut.FetchSsrDataAsync("dash1", layout);
        var cached = await sut.FetchSsrDataAsync("dash1", layout);
        await sut.FetchSsrDataAsync("dash1", layout, bypassCache: true);

        cached.SourceStatuses[DataSourceKeys.Todo("todo.list")].FromCache.Should().BeTrue();
        _todoDataProvider.Verify(
            p => p.FetchTodoItemsAsync("dash1", "todo.list", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task FetchSsrDataAsync_FailedSource_ReportsErrorStatusWithoutInventingData()
    {
        _calendarDataProvider
            .Setup(p => p.FetchCalendarEventsAsync("dash1", "calendar.a", 168, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<List<CalendarEvent>>("calendar unavailable"));
        var sut = CreateSut();

        var result = await sut.FetchSsrDataAsync(
            "dash1",
            LayoutWith(Widget("calendar", new { entityId = "calendar.a" })));

        result.CalendarEvents.Should().NotContainKey("calendar.a");
        result.SourceStatuses[DataSourceKeys.Calendar("calendar.a")]
            .Should().Match<DataSourceStatus>(status =>
                status.State == "error" && status.Error == "calendar unavailable");
    }

    [Fact]
    public void WidgetDataPlan_RequestsOnlySourcesActuallyUsedByWidgets()
    {
        var layout = LayoutWith(
            Widget("todo", new { entityId = "todo.list" }, "todo"),
            Widget("rss-feed", new { entityId = "event.feed" }, "rss"),
            Widget("graph", new { series = new[] { new { entityId = "sensor.temp" } }, period = "7d" }, "graph"));

        var plan = WidgetDataPlan.Create(layout);

        plan.EntityStateIds.Should().BeEmpty();
        plan.TodoEntityIds.Should().BeEquivalentTo(["todo.list"]);
        plan.RssEntityIds.Should().BeEquivalentTo(["event.feed"]);
        plan.HistoryHoursByEntityId.Should().Contain("sensor.temp", 168);
    }
}
