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

namespace EPaperDashboard.UnitTests.Services.Ai;

public class AiDashboardGenerationServiceTests
{
    private readonly Mock<IAiServiceFactory> _aiServiceFactory = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IDashboardRepository> _dashboardRepository = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero));

    private AiDashboardGenerationService CreateSut()
    {
        var entityStateProvider = new Mock<IEntityStateProvider>();
        entityStateProvider.Setup(p => p.FetchAllEntityStatesAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<List<EntityState>, string>(new List<EntityState>()));
        var todoDataProvider = new Mock<ITodoDataProvider>();
        todoDataProvider.Setup(p => p.FetchAllTodoItemsAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<TodoItem>>, string>(new Dictionary<string, List<TodoItem>>()));
        var calendarDataProvider = new Mock<ICalendarDataProvider>();
        calendarDataProvider.Setup(p => p.FetchAllCalendarEventsAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<CalendarEvent>>, string>(new Dictionary<string, List<CalendarEvent>>()));
        var weatherForecastProvider = new Mock<IWeatherForecastProvider>();
        weatherForecastProvider.Setup(p => p.FetchAllWeatherForecastsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<WeatherForecast>>, string>(new Dictionary<string, List<WeatherForecast>>()));
        var rssFeedDataProvider = new Mock<IRssFeedDataProvider>();
        rssFeedDataProvider.Setup(p => p.FetchAllRssFeedEntriesAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Success<Dictionary<string, List<RssFeedEntry>>, string>(new Dictionary<string, List<RssFeedEntry>>()));

        var dataFetcher = new AiDataFetcher(
            entityStateProvider.Object,
            todoDataProvider.Object,
            calendarDataProvider.Object,
            weatherForecastProvider.Object,
            rssFeedDataProvider.Object,
            NullLogger<AiDataFetcher>.Instance);

        return new AiDashboardGenerationService(
            _aiServiceFactory.Object,
            dataFetcher,
            new AiResponseParser(NullLogger<AiResponseParser>.Instance),
            new WidgetValidator(NullLogger<WidgetValidator>.Instance),
            new WidgetLayoutEngine(),
            new GridPacker(NullLogger<GridPacker>.Instance),
            new DashboardService(_dashboardRepository.Object),
            new UserService(Mock.Of<IUserRepository>(), _dashboardRepository.Object),
            new AiPromptBuilder([], _timeProvider),
            _timeProvider,
            NullLogger<AiDashboardGenerationService>.Instance);
    }

    private static Dashboard CreateDashboard() => new()
    {
        Id = DashboardId.New(),
        Name = "Test",
        AiConfig = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant }
    };

    [Fact]
    public async Task GenerateAsync_AiNotConfigured_ReturnsFailureAndStoresErrorOnDashboard()
    {
        var dashboard = CreateDashboard();
        dashboard.AiConfig = null;
        var sut = CreateSut();

        var result = await sut.GenerateAsync(dashboard);

        result.IsFailure.Should().BeTrue();
        dashboard.LastAiGenerationError.Should().NotBeNullOrEmpty();
        _dashboardRepository.Verify(r => r.Update(dashboard), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_AiServiceFactoryFails_ReturnsFailureAndStoresError()
    {
        _aiServiceFactory
            .Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>()))
            .Returns(Result.Failure<IAiService, string>("no connection configured"));
        var sut = CreateSut();
        var dashboard = CreateDashboard();

        var result = await sut.GenerateAsync(dashboard);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("no connection configured");
        dashboard.LastAiGenerationError.Should().Be("no connection configured");
    }

    [Fact]
    public async Task GenerateAsync_CompletionCallFails_ReturnsFailureAndStoresError()
    {
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>())).Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService
            .Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Failure<string, string>("LLM unreachable"));
        var sut = CreateSut();
        var dashboard = CreateDashboard();

        var result = await sut.GenerateAsync(dashboard);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("LLM unreachable");
    }

    [Fact]
    public async Task GenerateAsync_AllWidgetsInvalidAfterValidation_ReturnsFailure()
    {
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>())).Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService
            .Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Success<string, string>("""{"widgets": [{"type": "markdown", "config": {"content": ""}}]}"""));
        var sut = CreateSut();
        var dashboard = CreateDashboard();

        var result = await sut.GenerateAsync(dashboard);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("invalid after validation");
    }

    [Fact]
    public async Task GenerateAsync_SuccessfulFlow_PlacesWidgetsAndStampsGenerationTime()
    {
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>())).Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService
            .Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Success<string, string>("""{"widgets": [{"type": "markdown", "config": {"content": "hello world"}}]}"""));
        var sut = CreateSut();
        var dashboard = CreateDashboard();

        var result = await sut.GenerateAsync(dashboard);

        result.IsSuccess.Should().BeTrue();
        result.Value.Widgets.Should().ContainSingle(w => w.Type == "markdown");
        dashboard.AiGeneratedWidgets.Should().ContainSingle();
        dashboard.LastAiGenerationTime.Should().Be(_timeProvider.GetUtcNow());
        dashboard.LastAiGenerationError.Should().BeNull();
        _dashboardRepository.Verify(r => r.Update(dashboard), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_MalformedJsonRepairedSuccessfully_ReturnsRepairedWidgets()
    {
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>())).Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService
            .SetupSequence(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Success<string, string>("{ broken json"))
            .ReturnsAsync(Result.Success<string, string>("""{"widgets": [{"type": "markdown", "config": {"content": "fixed"}}]}"""));
        var sut = CreateSut();
        var dashboard = CreateDashboard();

        var result = await sut.GenerateAsync(dashboard);

        result.IsSuccess.Should().BeTrue();
        result.Value.Widgets.Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateAsync_PromptOverrideProvided_SetsDashboardAiPrompt()
    {
        _aiServiceFactory.Setup(f => f.Create(It.IsAny<AiConfig>(), It.IsAny<string>())).Returns(Result.Success<IAiService, string>(_aiService.Object));
        _aiService
            .Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Success<string, string>("""{"widgets": [{"type": "markdown", "config": {"content": "x"}}]}"""));
        var sut = CreateSut();
        var dashboard = CreateDashboard();

        await sut.GenerateAsync(dashboard, promptOverride: "Focus on weather");

        dashboard.AiPrompt.Should().Be("Focus on weather");
    }
}
