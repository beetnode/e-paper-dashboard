using System.Net;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class HomeAssistantAuthServiceTests
{
    private readonly Mock<IDeploymentStrategy> _deployment = new();
    private readonly Mock<IHttpClientFactory> _clients = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-11T12:00:00Z"));
    private readonly ProviderConnection _connection = new() { UserId = UserId.New(), PluginId = "home-assistant", Name = "Home" };

    public HomeAssistantAuthServiceTests()
    {
        _deployment.SetupGet(d => d.Mode).Returns(DeploymentMode.Standalone);
        _deployment.Setup(d => d.GetOAuthClientUri(It.IsAny<HttpContext?>())).Returns(new Uri("https://board.example.test/"));
    }

    private HomeAssistantAuthService CreateSut() => new(NullLogger<HomeAssistantAuthService>.Instance, _clients.Object, _deployment.Object, _time);

    [Fact]
    public void StartConnectionAuth_RedirectsBackToTheProviderCallback()
    {
        var result = CreateSut().StartConnectionAuth(_connection, "https://ha.example.test/", null);

        result.IsSuccess.Should().BeTrue();
        var url = new Uri(result.Value.Url);
        url.GetLeftPart(UriPartial.Path).Should().Be("https://ha.example.test/auth/authorize");
        var query = QueryHelpers.ParseQuery(url.Query);
        query["client_id"].ToString().Should().Be("https://board.example.test");
        query["redirect_uri"].ToString().Should().Be("https://board.example.test/api/providers/home-assistant/callback");
        query["state"].ToString().Should().Be(result.Value.State);
    }

    [Fact]
    public void StartConnectionAuth_InvalidHost_Fails()
    {
        CreateSut().StartConnectionAuth(_connection, "", null).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void StartConnectionAuth_UnknownClientUri_Fails()
    {
        _deployment.Setup(d => d.GetOAuthClientUri(It.IsAny<HttpContext?>())).Returns((Uri?)null);

        CreateSut().StartConnectionAuth(_connection, "https://ha.example.test", null).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteConnectionAuth_ExpiredState_FailsWithoutCallingHomeAssistant()
    {
        var sut = CreateSut();
        var state = sut.StartConnectionAuth(_connection, "https://ha.example.test", null).Value.State;
        _time.Advance(TimeSpan.FromMinutes(6));

        (await sut.CompleteConnectionAuthAsync("code", state, default)).IsFailure.Should().BeTrue();
        _clients.Verify(c => c.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompleteConnectionAuth_TamperedState_Fails()
    {
        var sut = CreateSut();
        var state = sut.StartConnectionAuth(_connection, "https://ha.example.test", null).Value.State;

        (await sut.CompleteConnectionAuthAsync("code", state[..^2] + "AA", default)).IsFailure.Should().BeTrue();
        _clients.Verify(c => c.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompleteConnectionAuth_RejectedCode_FailsWithoutLeakingResponse()
    {
        _clients.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new StaticHandler(HttpStatusCode.BadRequest, "secret-body")));
        var sut = CreateSut();
        var state = sut.StartConnectionAuth(_connection, "https://ha.example.test", null).Value.State;

        var result = await sut.CompleteConnectionAuthAsync("code", state, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotContain("secret-body");
    }

    [Fact]
    public async Task DashboardCallback_RejectsConnectionStates()
    {
        var sut = CreateSut();
        var state = sut.StartConnectionAuth(_connection, "https://ha.example.test", null).Value.State;

        var result = await sut.HandleCallback("code", state, null);

        result.IsSuccess.Should().BeFalse();
        result.AccessToken.Should().BeNull();
    }

    private sealed class StaticHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
