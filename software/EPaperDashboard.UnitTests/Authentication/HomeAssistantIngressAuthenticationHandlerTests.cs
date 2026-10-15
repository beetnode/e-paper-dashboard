using System.Security.Claims;
using System.Text.Encodings.Web;
using EPaperDashboard.Authentication;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Authentication;

public class HomeAssistantIngressAuthenticationHandlerTests
{
    private readonly Mock<IDeploymentStrategy> _deployment = new();

    private async Task<AuthenticateResult> AuthenticateAsync()
    {
        var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Setup(o => o.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());
        var handler = new HomeAssistantIngressAuthenticationHandler(options.Object, NullLoggerFactory.Instance, UrlEncoder.Default, _deployment.Object);
        await handler.InitializeAsync(
            new AuthenticationScheme(HomeAssistantIngressAuthenticationHandler.SchemeName, null, typeof(HomeAssistantIngressAuthenticationHandler)),
            new DefaultHttpContext());
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task NotAnIngressRequest_ReturnsNoResult()
    {
        _deployment.Setup(d => d.IsIngressRequest(It.IsAny<HttpContext>())).Returns(false);

        var result = await AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task IngressRequest_SignsInTheSharedSuperUser()
    {
        _deployment.Setup(d => d.IsIngressRequest(It.IsAny<HttpContext>())).Returns(true);

        var result = await AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        var principal = result.Principal!;
        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(Constants.HomeAssistantVirtualUserId.ToString());
        principal.FindFirst(Constants.IsSuperUserClaim)!.Value.Should().Be("true");
        principal.FindFirst(Constants.HomeAssistantIngressClaim)!.Value.Should().Be("true");
    }
}
