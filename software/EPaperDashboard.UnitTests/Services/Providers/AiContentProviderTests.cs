using System.Text.Json;
using CSharpFunctionalExtensions;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Ai;
using EPaperDashboard.Services.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Providers;

public class AiContentProviderTests
{
    private readonly Mock<IDashboardRepository> _dashboardRepository = new();
    private readonly Mock<IAiServiceFactory> _aiServiceFactory = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero));

    private AiContentProvider CreateSut(IEntityStateProvider? entityStateProvider = null)
    {
        var dataFetcher = new AiDataFetcher(
            entityStateProvider ?? StubEntityStateProvider(),
            StubTodoProvider(),
            StubCalendarProvider(),
            StubWeatherProvider(),
            StubRssProvider(),
            NullLogger<AiDataFetcher>.Instance);

        return new AiContentProvider(
            new DashboardService(_dashboardRepository.Object),
            new UserService(Mock.Of<IUserRepository>(), _dashboardRepository.Object),
            _aiServiceFactory.Object,
            dataFetcher,
            [],
            _timeProvider,
            NullLogger<AiContentProvider>.Instance);
    }

    private static IEntityStateProvider StubEntityStateProvider()
    {
        var mock = new Mock<IEntityStateProvider>();
        mock.Setup(p => p.FetchAllEntityStatesAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<List<EntityState>, string>(new List<EntityState>()));
        return mock.Object;
    }

    private static ITodoDataProvider StubTodoProvider()
    {
        var mock = new Mock<ITodoDataProvider>();
        mock.Setup(p => p.FetchAllTodoItemsAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<TodoItem>>, string>(new Dictionary<string, List<TodoItem>>()));
        return mock.Object;
    }

    private static ICalendarDataProvider StubCalendarProvider()
    {
        var mock = new Mock<ICalendarDataProvider>();
        mock.Setup(p => p.FetchAllCalendarEventsAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<CalendarEvent>>, string>(new Dictionary<string, List<CalendarEvent>>()));
        return mock.Object;
    }

    private static IWeatherForecastProvider StubWeatherProvider()
    {
        var mock = new Mock<IWeatherForecastProvider>();
        mock.Setup(p => p.FetchAllWeatherForecastsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<WeatherForecast>>, string>(new Dictionary<string, List<WeatherForecast>>()));
        return mock.Object;
    }

    private static IRssFeedDataProvider StubRssProvider()
    {
        var mock = new Mock<IRssFeedDataProvider>();
        mock.Setup(p => p.FetchAllRssFeedEntriesAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<RssFeedEntry>>, string>(new Dictionary<string, List<RssFeedEntry>>()));
        return mock.Object;
    }

    [Fact]
    public async Task GenerateContentAsync_InvalidDashboardId_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = await sut.GenerateContentAsync("not-an-id", "prompt", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Invalid dashboard ID");
    }

    [Fact]
    public async Task GenerateContentAsync_DashboardNotFound_ReturnsFailure()
    {
        var dashboardId = DashboardId.New();
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns((Dashboard?)null);
        var sut = CreateSut();

        var result = await sut.GenerateContentAsync(dashboardId.Value, "prompt", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Dashboard not found");
    }

    [Fact]
    public async Task GenerateContentAsync_AiNotConfigured_ReturnsFailure()
    {
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard { Id = dashboardId };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        var sut = CreateSut();

        var result = await sut.GenerateContentAsync(dashboardId.Value, "prompt", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("AI is not configured");
    }

    [Fact]
    public async Task GenerateContentAsync_AiConfigured_ReturnsGeneratedContent()
    {
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard { Id = dashboardId, AiConfig = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant } };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>()))
            .Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService.Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Result.Success<string, string>("Generated content"));
        var sut = CreateSut();

        var result = await sut.GenerateContentAsync(dashboardId.Value, "Tell me a story", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Generated content");
    }

    [Fact]
    public async Task GenerateAndCacheContentAsync_Success_StoresContentAndStampsTimestamp()
    {
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard { Id = dashboardId, AiConfig = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant } };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>()))
            .Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService.Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Result.Success<string, string>("cached text"));
        var sut = CreateSut();

        var result = await sut.GenerateAndCacheContentAsync(dashboardId.Value, "widget-1", "prompt", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dashboard.AiContentCache!["widget-1"].Should().Be("cached text");
        dashboard.LastAiContentCacheTime.Should().Be(_timeProvider.GetUtcNow());
        _dashboardRepository.Verify(r => r.Update(dashboard), Times.Once);
    }

    [Fact]
    public async Task GenerateAndCacheContentAsync_Failure_DoesNotTouchCache()
    {
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard { Id = dashboardId, AiConfig = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant } };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>()))
            .Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService.Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Result.Failure<string, string>("llm error"));
        var sut = CreateSut();

        var result = await sut.GenerateAndCacheContentAsync(dashboardId.Value, "widget-1", "prompt", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        dashboard.AiContentCache.Should().BeNull();
        _dashboardRepository.Verify(r => r.Update(It.IsAny<Dashboard>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAndCacheContentAsync_NoDataScope_DoesNotFetchDashboardData()
    {
        var dashboardId = DashboardId.New();
        var entityStateProvider = new Mock<IEntityStateProvider>();
        var dashboard = new Dashboard
        {
            Id = dashboardId,
            AiConfig = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant },
            LayoutConfig = new LayoutConfig
            {
                Widgets =
                [
                    new WidgetConfig
                    {
                        Id = "widget-1",
                        Type = "ai-content",
                        Config = JsonSerializer.SerializeToElement(new { prompt = "quote", dataScope = "none" })
                    }
                ]
            }
        };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>()))
            .Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService.Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Result.Success<string, string>("private content"));
        var sut = CreateSut(entityStateProvider.Object);

        var result = await sut.GenerateAndCacheContentAsync(
            dashboardId.Value,
            "widget-1",
            "quote",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entityStateProvider.Verify(
            provider => provider.FetchAllEntityStatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void GetCachedContent_NoCacheEntry_ReturnsNull()
    {
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard { Id = dashboardId };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        var sut = CreateSut();

        sut.GetCachedContent(dashboardId.Value, "widget-1").Should().BeNull();
    }

    [Fact]
    public void GetCachedContent_CachedValuePresent_ReturnsIt()
    {
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard { Id = dashboardId, AiContentCache = new Dictionary<string, string> { ["widget-1"] = "hello" } };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        var sut = CreateSut();

        sut.GetCachedContent(dashboardId.Value, "widget-1").Should().Be("hello");
    }

    [Fact]
    public async Task PreGenerateAllAsync_NoAiContentWidgets_DoesNotCallAiOrUpdateDashboard()
    {
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard
        {
            Id = dashboardId,
            AiConfig = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant },
            LayoutConfig = new LayoutConfig { Widgets = [new WidgetConfig { Type = "markdown" }] }
        };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        var sut = CreateSut();

        await sut.PreGenerateAllAsync(dashboardId.Value, CancellationToken.None);

        _aiServiceFactory.Verify(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>()), Times.Never);
        _dashboardRepository.Verify(r => r.Update(It.IsAny<Dashboard>()), Times.Never);
    }

    [Fact]
    public async Task PreGenerateAllAsync_AiContentWidgetWithPrompt_GeneratesAndCachesForEachWidget()
    {
        var dashboardId = DashboardId.New();
        var widgetConfig = JsonSerializer.SerializeToElement(new { prompt = "summarize" });
        var dashboard = new Dashboard
        {
            Id = dashboardId,
            AiConfig = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant },
            LayoutConfig = new LayoutConfig
            {
                Widgets = [new WidgetConfig { Id = "w1", Type = "ai-content", Config = widgetConfig }]
            }
        };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>()))
            .Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService.Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Result.Success<string, string>("generated summary"));
        var sut = CreateSut();

        await sut.PreGenerateAllAsync(dashboardId.Value, CancellationToken.None);

        dashboard.AiContentCache!["w1"].Should().Be("generated summary");
        dashboard.LastAiContentCacheTime.Should().Be(_timeProvider.GetUtcNow());
        _dashboardRepository.Verify(r => r.Update(dashboard), Times.Once);
    }

    [Fact]
    public async Task PreGenerateAllAsync_WidgetMissingPrompt_IsSkippedWithoutCallingAi()
    {
        var dashboardId = DashboardId.New();
        var widgetConfig = JsonSerializer.SerializeToElement(new { });
        var dashboard = new Dashboard
        {
            Id = dashboardId,
            AiConfig = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant },
            LayoutConfig = new LayoutConfig
            {
                Widgets = [new WidgetConfig { Id = "w1", Type = "ai-content", Config = widgetConfig }]
            }
        };
        _dashboardRepository.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
        var sut = CreateSut();

        await sut.PreGenerateAllAsync(dashboardId.Value, CancellationToken.None);

        _aiServiceFactory.Verify(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>()), Times.Never);
    }
}
