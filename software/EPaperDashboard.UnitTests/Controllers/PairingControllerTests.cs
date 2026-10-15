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

    private PairingController CreateSut(Uri? clientUri = null)
    {
        var deviceService = new DeviceService(_deviceRepository.Object);
        var configuration = new Mock<IEnvironmentConfiguration>();
        configuration.SetupGet(c => c.ClientUri).Returns(clientUri ?? new Uri("http://dashboard.local:8129"));
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

    [Fact]
    public void StartPairing_ReturnsCodeAndExpiry()
    {
        var sut = CreateSut().WithUser(UserId.New());

        var result = sut.StartPairing();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<StartPairingResponse>().Subject;
        response.Code.Should().MatchRegex("^[0-9A-Z]{6}$");
        response.ExpiresAt.Should().Be(_timeProvider.GetUtcNow().AddMinutes(5));
    }

    [Fact]
    public void RegisterDevice_MissingCodeOrIdentifier_ReturnsBadRequest()
    {
        var sut = CreateSut();

        var result = sut.RegisterDevice(new RegisterDeviceRequest("", "device-1", null, null, null));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void RegisterDevice_UnknownCode_ReturnsNotFound()
    {
        _pairingSessionRepository.Setup(r => r.FindByCode("BADCOD")).Returns((PairingSession?)null);
        var sut = CreateSut();

        var result = sut.RegisterDevice(new RegisterDeviceRequest("BADCOD", "device-1", null, null, null));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void RegisterDevice_ExpiredCode_ReturnsBadRequest()
    {
        var session = new PairingSession { Code = "ABC123", ExpiresAt = _timeProvider.GetUtcNow().AddMinutes(-1) };
        _pairingSessionRepository.Setup(r => r.FindByCode("ABC123")).Returns(session);
        var sut = CreateSut();

        var result = sut.RegisterDevice(new RegisterDeviceRequest("ABC123", "device-1", null, null, null));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void RegisterDevice_SessionAlreadyCompleted_ReturnsBadRequest()
    {
        var session = new PairingSession
        {
            Code = "ABC123",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            Status = PairingStatus.Completed
        };
        _pairingSessionRepository.Setup(r => r.FindByCode("ABC123")).Returns(session);
        var sut = CreateSut();

        var result = sut.RegisterDevice(new RegisterDeviceRequest("ABC123", "device-1", null, null, null));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void RegisterDevice_NewDevice_CreatesDeviceRecord()
    {
        var session = new PairingSession
        {
            Code = "ABC123",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            Status = PairingStatus.Pending,
            UserId = UserId.New()
        };
        _pairingSessionRepository.Setup(r => r.FindByCode("ABC123")).Returns(session);
        _deviceRepository.Setup(r => r.FindByIdentifier("device-1")).Returns((Device?)null);
        var sut = CreateSut();

        var result = sut.RegisterDevice(new RegisterDeviceRequest("ABC123", "device-1", "My Device", null, null));

        result.Should().BeOfType<OkObjectResult>();
        _deviceRepository.Verify(r => r.Insert(It.Is<Device>(d =>
            d.DeviceIdentifier == "device-1" && d.Name == "My Device" && d.UserId == session.UserId)), Times.Once);
    }

    [Fact]
    public void RegisterDevice_ExistingDeviceReRegisteredBySameOwner_UpdatesDeviceButKeepsDashboardAssignment()
    {
        var userId = UserId.New();
        var existingDashboardId = DashboardId.New();
        var session = new PairingSession
        {
            Code = "ABC123",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            Status = PairingStatus.Pending,
            UserId = userId
        };
        var existingDevice = new Device { UserId = userId, DeviceIdentifier = "device-1", DashboardId = existingDashboardId };
        _pairingSessionRepository.Setup(r => r.FindByCode("ABC123")).Returns(session);
        _deviceRepository.Setup(r => r.FindByIdentifier("device-1")).Returns(existingDevice);
        var sut = CreateSut();

        sut.RegisterDevice(new RegisterDeviceRequest("ABC123", "device-1", null, null, null));

        existingDevice.DashboardId.Should().Be(existingDashboardId);
        _deviceRepository.Verify(r => r.Update(existingDevice), Times.Once);
    }

    [Fact]
    public void RegisterDevice_ExistingDeviceClaimedByNewOwner_ReturnsConflict()
    {
        var previousOwnerId = UserId.New();
        var newOwnerId = UserId.New();
        var session = new PairingSession
        {
            Code = "ABC123",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            Status = PairingStatus.Pending,
            UserId = newOwnerId
        };
        var existingDevice = new Device
        {
            UserId = previousOwnerId,
            DeviceIdentifier = "device-1",
            DashboardId = DashboardId.New(),
            FirmwareVersion = "1.0.0",
            LastSeenAt = _timeProvider.GetUtcNow()
        };
        _pairingSessionRepository.Setup(r => r.FindByCode("ABC123")).Returns(session);
        _deviceRepository.Setup(r => r.FindByIdentifier("device-1")).Returns(existingDevice);
        var sut = CreateSut();

        var result = sut.RegisterDevice(new RegisterDeviceRequest("ABC123", "device-1", null, null, null));

        result.Should().BeOfType<ConflictObjectResult>();
        existingDevice.UserId.Should().Be(previousOwnerId);
        _deviceRepository.Verify(r => r.Update(It.IsAny<Device>()), Times.Never);
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
