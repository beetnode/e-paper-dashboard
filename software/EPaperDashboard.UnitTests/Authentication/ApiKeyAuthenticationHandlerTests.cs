using System.Text.Encodings.Web;
using EPaperDashboard.Authentication;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
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

public class ApiKeyAuthenticationHandlerTests
{
    private static async Task<(AuthenticateResult result, DefaultHttpContext httpContext)> AuthenticateAsync(
        Mock<IDeviceRepository> deviceRepository, string? apiKeyHeaderValue)
    {
        var deviceService = new DeviceService(deviceRepository.Object);

        var optionsMonitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Setup(o => o.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

        var handler = new ApiKeyAuthenticationHandler(
            optionsMonitor.Object, NullLoggerFactory.Instance, UrlEncoder.Default, deviceService);

        var httpContext = new DefaultHttpContext();
        if (apiKeyHeaderValue is not null)
        {
            httpContext.Request.Headers[HttpHeaderNames.ApiKeyHeaderName] = apiKeyHeaderValue;
        }

        var scheme = new AuthenticationScheme(ApiKeyAuthenticationHandler.SchemeName, null, typeof(ApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, httpContext);

        return (await handler.AuthenticateAsync(), httpContext);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingApiKeyHeader_Fails()
    {
        var (result, _) = await AuthenticateAsync(new Mock<IDeviceRepository>(), apiKeyHeaderValue: null);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Missing or empty API Key");
    }

    [Fact]
    public async Task AuthenticateAsync_ApiKeyDoesNotMatchAnyDevice_Fails()
    {
        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository.Setup(r => r.FindByApiKey("bad-key")).Returns((Device?)null);

        var (result, _) = await AuthenticateAsync(deviceRepository, "bad-key");

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Invalid API Key");
    }

    [Fact]
    public async Task AuthenticateAsync_ApiKeyMatchesDevice_SucceedsWithApiKeyClaim()
    {
        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository.Setup(r => r.FindByApiKey("good-key")).Returns(new Device { ApiKey = "good-key" });

        var (result, _) = await AuthenticateAsync(deviceRepository, "good-key");

        result.Succeeded.Should().BeTrue();
        result.Principal!.HasClaim("ApiKey", "good-key").Should().BeTrue();
    }
}
