using EPaperDashboard.Controllers;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.UnitTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.UnitTests.Controllers;

public class PairingControllerTests
{
    private readonly Mock<IPairingSessionRepository> _pairingSessionRepository = new();
    private readonly Mock<IDeviceRepository> _deviceRepository = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero));

    private PairingController CreateSut(Uri? clientUri = null, int devicePublicPort = 8129)
    {
        var deviceService = new DeviceService(_deviceRepository.Object);
        var configuration = new Mock<IEnvironmentConfiguration>();
        configuration.SetupGet(c => c.ClientUri).Returns(clientUri ?? new Uri("http://dashboard.local:8129"));
        configuration.SetupGet(c => c.DevicePublicPort).Returns(devicePublicPort);
        return new PairingController(
            new PairingService(
                _pairingSessionRepository.Object,
                deviceService,
                _timeProvider,
                ImmediateUnitOfWork.Instance),
            deviceService,
            configuration.Object);
    }

    [Fact]
    public void GetPairingConfiguration_ReturnsRemoteDeviceUrl()
    {
        var sut = CreateSut(new Uri("https://devices.example.com/dashboard"));

        var result = sut.GetPairingConfiguration();

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PairingConfigurationResponse>().Subject;
        response.ClientUrl.Should().Be("https://devices.example.com/dashboard");
    }

    [Theory]
    [InlineData("http://192.168.1.10:8128", 8129, "192.168.1.10:8129")]
    [InlineData("https://izboard.example.com/dashboard", 9443, "izboard.example.com:9443")]
    [InlineData("http://[fd00::10]:8128", 8129, "[fd00::10]:8129")]
    public void GetPairingConfiguration_ServerAddressIsClientHostWithDevicePort(string clientUrl, int devicePort, string expected)
    {
        var sut = CreateSut(new Uri(clientUrl), devicePort);

        var result = sut.GetPairingConfiguration();

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PairingConfigurationResponse>().Subject;
        response.ServerAddress.Should().Be(expected);
    }

    [Fact]
    public void GetPairingStatus_MissingCode_ReturnsBadRequest()
    {
        var sut = CreateSut().WithUser(UserId.New());

        var result = sut.GetPairingStatus("");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void GetPairingStatus_UnknownCode_ReturnsNotFound()
    {
        _pairingSessionRepository.Setup(r => r.FindByCode("XYZ")).Returns((PairingSession?)null);
        var sut = CreateSut().WithUser(UserId.New());

        var result = sut.GetPairingStatus("XYZ");

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void GetPairingStatus_SessionBelongsToDifferentUser_ReturnsForbid()
    {
        var session = new PairingSession { Code = "XYZ", UserId = UserId.New() };
        _pairingSessionRepository.Setup(r => r.FindByCode("XYZ")).Returns(session);
        var sut = CreateSut().WithUser(UserId.New());

        var result = sut.GetPairingStatus("XYZ");

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public void GetPairingStatus_OwnedSession_ReturnsStatusAndDeviceIdentifier()
    {
        var userId = UserId.New();
        var session = new PairingSession { Code = "XYZ", UserId = userId, Status = PairingStatus.Completed, DeviceIdentifier = "device-1" };
        _pairingSessionRepository.Setup(r => r.FindByCode("XYZ")).Returns(session);
        var sut = CreateSut().WithUser(userId);

        var result = sut.GetPairingStatus("XYZ");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<PairingStatusResponse>().Subject;
        response.Status.Should().Be("completed");
        response.DeviceIdentifier.Should().Be("device-1");
    }

    [Fact]
    public void GetPairingStatus_ClaimedSession_WaitsForDeviceAcknowledgement()
    {
        var userId = UserId.New();
        var session = new PairingSession
        {
            Code = "ABC123",
            UserId = userId,
            Status = PairingStatus.Claimed,
            ExpiresAt = _timeProvider.GetUtcNow().AddMinutes(2),
            DeviceIdentifier = "device-1"
        };
        _pairingSessionRepository.Setup(r => r.FindByCode("ABC123")).Returns(session);
        var sut = CreateSut().WithUser(userId);

        var result = sut.GetPairingStatus("ABC123");

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PairingStatusResponse>().Subject;
        response.Status.Should().Be("claimed");
        response.ExpiresAt.Should().Be(session.ExpiresAt);
    }

    [Fact]
    public void GetPairingStatus_ExpiredUnacknowledgedSession_ReturnsGone()
    {
        var userId = UserId.New();
        var session = new PairingSession
        {
            Code = "ABC123",
            UserId = userId,
            Status = PairingStatus.Claimed,
            ExpiresAt = _timeProvider.GetUtcNow().AddSeconds(-1)
        };
        _pairingSessionRepository.Setup(r => r.FindByCode("ABC123")).Returns(session);
        var sut = CreateSut().WithUser(userId);

        var result = sut.GetPairingStatus("ABC123");

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(410);
    }
}
