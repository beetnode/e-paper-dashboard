using System.Security.Claims;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

// The strategy's constructor requires SUPERVISOR_TOKEN to be set (it throws otherwise), so each
// test sets it for the duration of the test instance and restores the previous value afterward.
public class HomeAssistantAddonStrategyTests : IDisposable
{
    private readonly string? _originalSupervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");

    public HomeAssistantAddonStrategyTests()
    {
        Environment.SetEnvironmentVariable("SUPERVISOR_TOKEN", "test-supervisor-token");
    }

    public void Dispose() => Environment.SetEnvironmentVariable("SUPERVISOR_TOKEN", _originalSupervisorToken);

    private static HomeAssistantAddonStrategy CreateSut()
    {
        var configuration = new Mock<IEnvironmentConfiguration>();
        configuration.SetupGet(c => c.ClientUri).Returns(new Uri("http://homeassistant.local:8129"));
        return new(NullLogger<HomeAssistantAddonStrategy>.Instance, configuration.Object);
    }

    [Fact]
    public void GetOAuthClientUri_NoIngressHeader_ReturnsNull()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();

        sut.GetOAuthClientUri(context).Should().BeNull();
    }

    [Fact]
    public void GetOAuthClientUri_IngressHeaderPresent_BuildsUriFromBrowserOriginAndIngressPath()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();
        context.Request.Headers[Constants.IngressPathHeader] = "/api/hassio_ingress/abc123";
        context.Items["BrowserOrigin"] = "http://homeassistant.local:8123";

        var result = sut.GetOAuthClientUri(context);

        result!.ToString().Should().Be("http://homeassistant.local:8123/api/hassio_ingress/abc123");
    }

    [Fact]
    public void GetOAuthClientUri_NoBrowserOriginItem_FallsBackToDefaultHomeAssistantOrigin()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();
        context.Request.Headers[Constants.IngressPathHeader] = "/ingress/x";

        var result = sut.GetOAuthClientUri(context);

        result!.ToString().Should().StartWith("http://homeassistant/ingress/x");
    }

    [Fact]
    public void AuthenticateViaIngress_NoIngressHeader_ReturnsNull()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();

        sut.AuthenticateViaIngress(context).Should().BeNull();
    }

    [Fact]
    public void AuthenticateViaIngress_IngressHeaderPresent_ReturnsPrincipalWithAdminAndSuperUserClaims()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();
        context.Request.Headers[Constants.IngressPathHeader] = "/ingress/x";

        var principal = sut.AuthenticateViaIngress(context);

        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(Constants.HomeAssistantVirtualUserId.ToString());
        principal.FindFirst(Constants.IsSuperUserClaim)!.Value.Should().Be("true");
        principal.FindFirst(Constants.HomeAssistantIngressClaim)!.Value.Should().Be("true");
    }

    [Fact]
    public async Task ProcessIngressPathAsync_NoIngressHeader_ReturnsFalseWithoutModifyingRequest()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();
        context.Request.Path = "/dashboard";
        var originalPath = context.Request.Path;

        var handled = await sut.ProcessIngressPathAsync(context, Mock.Of<IWebHostEnvironment>());

        handled.Should().BeFalse();
        context.Request.Path.Should().Be(originalPath);
    }

    [Fact]
    public async Task ProcessIngressPathAsync_IngressHeaderPresent_SetsPathBaseAndStripsPrefix()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();
        context.Request.Headers[Constants.IngressPathHeader] = "/api/hassio_ingress/abc123";
        context.Request.Path = "/api/hassio_ingress/abc123/dashboard/5";

        await sut.ProcessIngressPathAsync(context, Mock.Of<IWebHostEnvironment>());

        context.Request.PathBase.Value.Should().Be("/api/hassio_ingress/abc123");
        context.Request.Path.Value.Should().Be("/dashboard/5");
        context.Items["IngressPath"].Should().Be("/api/hassio_ingress/abc123");
    }

    [Fact]
    public async Task ProcessIngressPathAsync_IngressPathEqualsFullRequestPath_RewritesPathToRoot()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();
        context.Request.Headers[Constants.IngressPathHeader] = "/ingress/x";
        context.Request.Path = "/ingress/x";

        await sut.ProcessIngressPathAsync(context, Mock.Of<IWebHostEnvironment>());

        context.Request.Path.Value.Should().Be("/");
    }

    [Fact]
    public void GetConfigDirectory_ReturnsInjectedEnvironmentConfigurationValue()
    {
        var environmentConfiguration = new Mock<IEnvironmentConfiguration>();
        environmentConfiguration.SetupGet(c => c.ConfigDir).Returns("/custom/data");
        var sut = new HomeAssistantAddonStrategy(NullLogger<HomeAssistantAddonStrategy>.Instance, environmentConfiguration.Object);

        sut.GetConfigDirectory().Should().Be("/custom/data");
    }

    [Fact]
    public void ValidateConfiguration_WithClientUri_Succeeds()
    {
        var sut = CreateSut();

        sut.ValidateConfiguration().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateConfiguration_WithRemoteDeviceEndpoint_Succeeds()
    {
        var configuration = new Mock<IEnvironmentConfiguration>();
        configuration.SetupGet(c => c.ClientUri).Returns(new Uri("https://devices.example.com/izboard"));
        var sut = new HomeAssistantAddonStrategy(
            NullLogger<HomeAssistantAddonStrategy>.Instance,
            configuration.Object);

        sut.ValidateConfiguration().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateConfiguration_WithoutClientUri_Fails()
    {
        var sut = new HomeAssistantAddonStrategy(
            NullLogger<HomeAssistantAddonStrategy>.Instance,
            Mock.Of<IEnvironmentConfiguration>());

        var result = sut.ValidateConfiguration();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("CLIENT_URL");
    }

    [Fact]
    public void ValidateConfiguration_WithIngressUrl_Fails()
    {
        var configuration = new Mock<IEnvironmentConfiguration>();
        configuration.SetupGet(c => c.ClientUri).Returns(
            new Uri("http://homeassistant.local:8123/api/hassio_ingress/example?auth=token"));
        var sut = new HomeAssistantAddonStrategy(
            NullLogger<HomeAssistantAddonStrategy>.Instance,
            configuration.Object);

        sut.ValidateConfiguration().IsFailure.Should().BeTrue();
    }
}
