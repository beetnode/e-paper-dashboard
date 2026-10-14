using System.Net;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;
using FluentAssertions;
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

    private static DefaultHttpContext IngressContext(string ingressPath = "/api/hassio_ingress/abc123", string remoteAddress = "172.30.32.2")
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[Constants.IngressPathHeader] = ingressPath;
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        context.Connection.LocalPort = 8128;
        return context;
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
        var context = IngressContext();
        context.Items["BrowserOrigin"] = "http://homeassistant.local:8123";

        var result = sut.GetOAuthClientUri(context);

        result!.ToString().Should().Be("http://homeassistant.local:8123/api/hassio_ingress/abc123");
    }

    [Fact]
    public void IsIngressRequest_FromSupervisor_ReturnsTrue()
    {
        CreateSut().IsIngressRequest(IngressContext()).Should().BeTrue();
    }

    [Fact]
    public void IsIngressRequest_ForgedHeaderFromAnotherAddress_ReturnsFalse()
    {
        CreateSut().IsIngressRequest(IngressContext(remoteAddress: "172.30.33.5")).Should().BeFalse();
    }

    [Fact]
    public void IsIngressRequest_MalformedIngressPath_ReturnsFalse()
    {
        CreateSut().IsIngressRequest(IngressContext("/api/hassio_ingress/x\"><script>")).Should().BeFalse();
    }

    [Fact]
    public void IsIngressRequest_OnDevicePort_ReturnsFalse()
    {
        var context = IngressContext();
        context.Connection.LocalPort = Constants.DefaultDevicePort;

        CreateSut().IsIngressRequest(context).Should().BeFalse();
    }

    [Fact]
    public void RewriteIngressPath_SetsPathBaseAndStripsPrefix()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/hassio_ingress/abc123/dashboard/5";

        HomeAssistantAddonStrategy.RewriteIngressPath(context, "/api/hassio_ingress/abc123");

        context.Request.PathBase.Value.Should().Be("/api/hassio_ingress/abc123");
        context.Request.Path.Value.Should().Be("/dashboard/5");
    }

    [Fact]
    public void RewriteIngressPath_IngressPathEqualsFullRequestPath_RewritesPathToRoot()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/hassio_ingress/abc123";

        HomeAssistantAddonStrategy.RewriteIngressPath(context, "/api/hassio_ingress/abc123");

        context.Request.Path.Value.Should().Be("/");
    }

    [Fact]
    public void WithBaseHref_SetsTheIngressPathAsBase()
    {
        HomeAssistantAddonStrategy.WithBaseHref("<head><base href=\"/\"></head>", "/api/hassio_ingress/abc123")
            .Should().Be("<head><base href=\"/api/hassio_ingress/abc123/\"></head>");
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
