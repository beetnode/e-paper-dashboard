using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Components.Playwright;
using EPaperDashboard.Services.Rendering;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Rendering;

public sealed class PageToImageRenderingServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"page-rendering-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task UnexpectedFailureDoesNotExposeCredentialsOrPaths()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.FindById(It.IsAny<UserId>()))
            .Throws(new InvalidOperationException("/private/server-data https://private.invalid/?access_token=synthetic-test-secret"));
        var result = await Create(users.Object).RenderHtmlAsync(UserId.New(), "<html></html>", new Size(200, 100));
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Rendering failed. Check Rendering settings and try again.");
    }

    [Fact]
    public async Task DisabledAccountKeepsActionableSafeMessage()
    {
        var result = await Create(Mock.Of<IUserRepository>()).RenderHtmlAsync(UserId.New(), "<html></html>", new Size(200, 100));
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Enable rendering in Rendering settings.");
    }

    [Fact]
    public async Task MissingComponentIsNotInstalledByRenderRequest()
    {
        var owner = UserId.New();
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.FindById(owner)).Returns(new User { Id = owner });
        users.Setup(r => r.GetRenderingEnabled(owner)).Returns(true);
        var environment = new Mock<IEnvironmentConfiguration>();
        environment.SetupGet(e => e.ConfigDir).Returns(_directory);
        var manager = new PlaywrightComponentManager(Mock.Of<IHttpClientFactory>(), environment.Object,
            new ConfigurationBuilder().Build(), NullLogger<PlaywrightComponentManager>.Instance);

        var result = await Create(users.Object, manager, new PlaywrightComponentRuntime())
            .RenderHtmlAsync(owner, "<html></html>", new Size(200, 100));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Rendering is not installed on this server. Enable rendering in Rendering settings.");
        manager.GetStatus().State.Should().Be(PlaywrightComponentState.NotInstalled);
    }

    private static PageToImageRenderingService Create(IUserRepository users,
        PlaywrightComponentManager? manager = null, PlaywrightComponentRuntime? runtime = null)
    {
        var deployment = new Mock<IDeploymentStrategy>();
        deployment.SetupGet(d => d.Mode).Returns(DeploymentMode.Standalone);
        var access = new RenderingAccessService(users, Mock.Of<IDashboardRepository>(), deployment.Object,
            new UserRenderScheduler(new ConfigurationBuilder().Build()));
        // Access failures occur before any component, HTTP, or image work is permitted.
        return new PageToImageRenderingService(Mock.Of<IHttpClientFactory>(), Mock.Of<IImageFactory>(), manager!, runtime!,
            NullLogger<PageToImageRenderingService>.Instance, access);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
