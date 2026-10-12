using EPaperDashboard.Services.Plugins.HomeAssistant;
using CSharpFunctionalExtensions;
using EPaperDashboard.Controllers;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Providers;
using EPaperDashboard.Services.Rendering;
using EPaperDashboard.UnitTests.TestSupport;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using DashboardDesignerPreviewRequest = EPaperDashboard.Models.Rendering.DashboardDesignerPreviewRequest;
using DashboardDesignerPreviewResponse = EPaperDashboard.Models.Rendering.DashboardDesignerPreviewResponse;
using RenderingLayoutConfig = EPaperDashboard.Models.Rendering.LayoutConfig;
using SsrData = EPaperDashboard.Models.Rendering.SsrData;

namespace EPaperDashboard.UnitTests.Controllers;

public class DashboardSsrControllerTests
{
    private readonly Mock<IDashboardRepository> _dashboardRepository = new();
    private readonly Mock<ISsrDataProvider> _ssrDataProvider = new();

    private DashboardSsrController CreateSut()
    {
        _ssrDataProvider
            .Setup(provider => provider.FetchSsrDataAsync(
                It.IsAny<string>(),
                It.IsAny<RenderingLayoutConfig>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new SsrData());

        var environment = Mock.Of<IWebHostEnvironment>();
        var iconRegistry = new FontAwesomeIconRegistry(
            environment,
            NullLogger<FontAwesomeIconRegistry>.Instance);
        var renderingUtilities = new RenderingUtilities(SystemFonts.Families.First(), iconRegistry);
        var imageRenderer = new DashboardImageRenderingService(
            _ssrDataProvider.Object,
            NullLogger<DashboardImageRenderingService>.Instance,
            renderingUtilities,
            [],
            new MemoryCache(new MemoryCacheOptions()));

        return new DashboardSsrController(
            new DashboardService(_dashboardRepository.Object),
            imageRenderer,
            new DesignerPreviewImageStore(TimeProvider.System),
            Mock.Of<IPageToImageRenderingService>(),
            new HomeAssistantDashboardTargetResolver(Mock.Of<IEnvironmentConfiguration>(),
                InMemoryProviderConnectionRepository.CreateStore()),
            TimeProvider.System);
    }

    [Fact]
    public async Task RenderTransientDashboardImage_UsesPostedUnsavedLayout()
    {
        var userId = UserId.New();
        var dashboardId = DashboardId.New();
        _dashboardRepository.Setup(repository => repository.FindById(dashboardId)).Returns(
            new Dashboard
            {
                Id = dashboardId,
                UserId = userId,
                LayoutConfig = new LayoutConfig { Width = 10, Height = 10 }
            });
        var sut = CreateSut().WithUser(userId);
        var transientLayout = new LayoutConfig
        {
            Width = 123,
            Height = 45,
            GridCols = 4,
            GridRows = 2
        };

        var result = await sut.RenderTransientDashboardImage(
            dashboardId.Value,
            transientLayout,
            "png",
            refresh: true);

        var file = result.Should().BeOfType<FileStreamResult>().Subject;
        using var image = await Image.LoadAsync<Rgba32>(file.FileStream);
        image.Width.Should().Be(123);
        image.Height.Should().Be(45);
    }

    [Fact]
    public async Task RenderTransientDashboardImage_RejectsUnsafeDimensions()
    {
        var userId = UserId.New();
        var dashboardId = DashboardId.New();
        _dashboardRepository.Setup(repository => repository.FindById(dashboardId)).Returns(
            new Dashboard { Id = dashboardId, UserId = userId });
        var sut = CreateSut().WithUser(userId);
        var transientLayout = new LayoutConfig
        {
            Width = 5000,
            Height = 45,
            GridCols = 4,
            GridRows = 2
        };

        var result = await sut.RenderTransientDashboardImage(
            dashboardId.Value,
            transientLayout,
            "png",
            refresh: true);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RenderDesignerPreview_EchoesRevisionAndReturnsRendererGeometry()
    {
        var userId = UserId.New();
        var dashboardId = DashboardId.New();
        _dashboardRepository.Setup(repository => repository.FindById(dashboardId)).Returns(
            new Dashboard
            {
                Id = dashboardId,
                UserId = userId,
                LayoutConfig = new LayoutConfig { Width = 10, Height = 10 }
            });
        var sut = CreateSut().WithUser(userId);
        var transientLayout = new LayoutConfig
        {
            Width = 120,
            Height = 60,
            GridCols = 4,
            GridRows = 2,
            CanvasPadding = 4,
            WidgetGap = 2,
            WidgetBorder = 1,
            WidgetPadding = 2,
            Widgets =
            [
                new WidgetConfig
                {
                    Id = "widget-1",
                    Type = "version",
                    Position = new WidgetPosition { X = 1, Y = 0, W = 2, H = 1 }
                }
            ]
        };

        var result = await sut.RenderDesignerPreview(
            dashboardId.Value,
            new DashboardDesignerPreviewRequest(transientLayout, Revision: 17));

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<DashboardDesignerPreviewResponse>().Subject;
        response.Revision.Should().Be(17);
        response.Width.Should().Be(120);
        response.Height.Should().Be(60);
        response.ImageUrl.Should().Contain($"/api/dashboards/{dashboardId.Value}/designer-preview/");
        var token = response.ImageUrl.Split('/')[5];
        var imageResult = sut.GetDesignerPreviewImage(dashboardId.Value, token);
        imageResult.Should().BeOfType<FileContentResult>()
            .Which.FileContents.Should().NotBeEmpty();
        response.Widgets.Should().ContainSingle(widget =>
            widget.Id == "widget-1"
            && widget.Bounds.Width > 0
            && widget.ContentBounds.Width < widget.Bounds.Width);
    }

    [Fact]
    public async Task GetTransientPreviewData_UsesPostedUnsavedLayout()
    {
        var userId = UserId.New();
        var dashboardId = DashboardId.New();
        _dashboardRepository.Setup(repository => repository.FindById(dashboardId)).Returns(
            new Dashboard
            {
                Id = dashboardId,
                UserId = userId,
                LayoutConfig = new LayoutConfig { Width = 10, Height = 10 }
            });
        var sut = CreateSut().WithUser(userId);
        var transientLayout = new LayoutConfig
        {
            Width = 123,
            Height = 45,
            GridCols = 4,
            GridRows = 2
        };

        var result = await sut.GetTransientPreviewData(dashboardId.Value, transientLayout);

        result.Should().BeOfType<OkObjectResult>();
        _ssrDataProvider.Verify(provider => provider.FetchSsrDataAsync(
            dashboardId.Value,
            It.Is<RenderingLayoutConfig>(layout => layout.Width == 123 && layout.Height == 45),
            It.IsAny<CancellationToken>(),
            true), Times.Once);
    }
}
