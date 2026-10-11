using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Rendering;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Rendering;

public sealed class PageToImageRenderingServiceTests
{
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

    private static PageToImageRenderingService Create(IUserRepository users)
    {
        var deployment = new Mock<IDeploymentStrategy>();
        deployment.SetupGet(d => d.Mode).Returns(DeploymentMode.Standalone);
        var access = new RenderingAccessService(users, Mock.Of<IDashboardRepository>(), deployment.Object,
            new UserRenderScheduler(new ConfigurationBuilder().Build()));
        // Both failures occur before any component, HTTP, or image work is permitted.
        return new PageToImageRenderingService(Mock.Of<IHttpClientFactory>(), Mock.Of<IImageFactory>(), null!, null!,
            NullLogger<PageToImageRenderingService>.Instance, access);
    }
}
