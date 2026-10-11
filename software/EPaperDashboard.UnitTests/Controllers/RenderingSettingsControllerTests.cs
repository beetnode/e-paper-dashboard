using System.Security.Claims;
using System.Text.Json;
using CSharpFunctionalExtensions;
using EPaperDashboard.Controllers;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Components.Playwright;
using EPaperDashboard.Services.Rendering;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Controllers;

public sealed class RenderingSettingsControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rendering-status-tests-{Guid.NewGuid():N}");
    private readonly Mock<IPageToImageRenderingService> _rendering = new();

    [Fact]
    public async Task StatusReturnsOnlyFieldsRequiredByUserSettings()
    {
        var controller = Create();
        var result = (OkObjectResult)await controller.GetStatus();
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("enabled", "component");
        json.GetProperty("component").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("state", "supported", "ready");
    }

    [Fact]
    public async Task TestDoesNotReturnRawWorkerFailure()
    {
        _rendering.Setup(r => r.RenderHtmlAsync(It.IsAny<UserId>(), It.IsAny<string>(), It.IsAny<Size>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IImage>("https://private.invalid/?access_token=synthetic-test-secret"));
        var result = (BadRequestObjectResult)await Create().Test();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().NotContain("synthetic-test-secret").And.NotContain("private.invalid");
        json.Should().Contain("Rendering test failed");
    }

    [Fact]
    public void DetailedComponentStatusRequiresAdministrator()
    {
        var method = typeof(PlaywrightComponentController).GetMethod(nameof(PlaywrightComponentController.GetStatus))!;
        method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Should().Contain(a => a.Policy == "SuperUserOnly");
    }

    private RenderingSettingsController Create()
    {
        var owner = UserId.New();
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.FindById(owner)).Returns(new User { Id = owner });
        var configuration = new ConfigurationBuilder().Build();
        var access = new RenderingAccessService(users.Object, Mock.Of<IDashboardRepository>(), Mock.Of<IDeploymentStrategy>(),
            new UserRenderScheduler(configuration));
        var environment = new Mock<IEnvironmentConfiguration>();
        environment.SetupGet(e => e.ConfigDir).Returns(_directory);
        var manager = new PlaywrightComponentManager(Mock.Of<IHttpClientFactory>(), environment.Object, configuration,
            NullLogger<PlaywrightComponentManager>.Instance);
        return new RenderingSettingsController(access, manager, new PlaywrightComponentRuntime(), _rendering.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner.ToString())], "test"))
            } }
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
