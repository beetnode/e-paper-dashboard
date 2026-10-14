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
    private readonly Mock<IUserRepository> _users = new();

    [Fact]
    public async Task StatusReturnsOnlyFieldsRequiredByUserSettings()
    {
        var controller = Create();
        var result = (OkObjectResult)await controller.GetStatus();
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("enabled", "allowed", "canManage", "component");
        json.GetProperty("component").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("state", "supported", "ready");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatusReportsWhetherUserManagesRendering(bool superUser)
    {
        var result = (OkObjectResult)await Create(superUser).GetStatus();
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.GetProperty("canManage").GetBoolean().Should().Be(superUser);
    }

    [Fact]
    public async Task RegularUserOptInDoesNotPrepareSharedComponent()
    {
        var result = await Create().SetEnabled(new RenderingSetting(true));

        // Unsupported platforms reject the opt-in before any installation decision.
        if (result is not OkObjectResult ok) return;
        var json = JsonSerializer.SerializeToElement(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.GetProperty("component").GetProperty("state").GetString().Should().Be(nameof(PlaywrightComponentState.NotInstalled));
        File.Exists(Path.Combine(_directory, "components", "playwright", "requested")).Should().BeFalse();
    }

    [Fact]
    public async Task OptInIsRejectedWhereRenderingIsUnsupported()
    {
        if (OperatingSystem.IsLinux()) return;
        var result = await Create().SetEnabled(new RenderingSetting(true));
        result.Should().BeOfType<BadRequestObjectResult>();
        _users.Verify(r => r.SetRenderingEnabled(It.IsAny<UserId>(), It.IsAny<bool>()), Times.Never);
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

    private IEnvironmentConfiguration Environment()
    {
        var environment = new Mock<IEnvironmentConfiguration>();
        environment.SetupGet(e => e.ConfigDir).Returns(_directory);
        return environment.Object;
    }

    private RenderingSettingsController Create(bool superUser = false)
    {
        var owner = UserId.New();
        var users = _users;
        users.Setup(r => r.FindById(owner)).Returns(new User { Id = owner });
        var configuration = new ConfigurationBuilder().Build();
        var access = new RenderingAccessService(
            new UserService(users.Object, Mock.Of<IDashboardRepository>(), deployment: Mock.Of<IDeploymentStrategy>()), Mock.Of<IDashboardRepository>(),
            new UserRenderScheduler(configuration));
        var manager = new PlaywrightComponentManager(Mock.Of<IHttpClientFactory>(), Environment(), configuration,
            NullLogger<PlaywrightComponentManager>.Instance);
        return new RenderingSettingsController(access, manager, new PlaywrightComponentRuntime(), _rendering.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, owner.ToString()),
                    new Claim(Constants.IsSuperUserClaim, superUser ? "true" : "false")], "test"))
            } }
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
