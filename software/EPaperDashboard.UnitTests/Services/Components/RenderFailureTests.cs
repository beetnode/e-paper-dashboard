using EPaperDashboard.Services.Components.Playwright;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Components;

public sealed class RenderFailureTests
{
    [Fact]
    public void ConnectionRefused_ExplainsReachabilityWithoutTheUrl()
    {
        var reason = RenderFailure.Describe(
            "net::ERR_CONNECTION_REFUSED at http://localhost:8123/\nCall log:\n  - navigating to \"http://localhost:8123/\"");

        reason.Should().Contain("connection refused").And.Contain("localhost").And.NotContain("8123");
    }

    [Fact]
    public void OtherNetworkErrors_KeepOnlyTheErrorCode()
    {
        RenderFailure.Describe("net::ERR_CERT_AUTHORITY_INVALID at https://secret-host.example/lovelace?token=abc")
            .Should().Be("The renderer could not load the Home Assistant page (ERR_CERT_AUTHORITY_INVALID).");
    }

    [Fact]
    public void Timeouts_AreReportedWithoutDetails()
    {
        RenderFailure.Describe("Timeout 60000ms exceeded.\nCall log:\n  - navigating to \"https://ha.example/lovelace\"")
            .Should().NotContain("ha.example").And.Contain("did not finish loading");
    }

    [Fact]
    public void InterruptedNavigation_PointsAtThePath()
    {
        RenderFailure.Describe("Navigation to \"http://ha/map\" is interrupted by another navigation to \"http://ha/auth\"")
            .Should().Contain("dashboard path").And.NotContain("http://ha");
    }

    [Theory]
    [InlineData("Page crashed")]
    [InlineData("Target page, context or browser has been closed")]
    public void BrowserCrashes_AreReported(string error)
    {
        RenderFailure.Describe(error).Should().Contain("browser stopped");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Page content: private text from https://ha.example")]
    public void UnknownErrors_AreGeneric(string? error)
    {
        RenderFailure.Describe(error).Should().Be(RenderFailure.Generic);
    }

    [Fact]
    public void WorkerException_ExposesSanitizedMessageAndKeepsRawForDiagnostics()
    {
        var exception = new RenderWorkerFailedException("net::ERR_NAME_NOT_RESOLVED at http://ha.internal/");

        exception.Message.Should().NotContain("ha.internal");
        exception.WorkerError.Should().Contain("ha.internal");
    }
}
