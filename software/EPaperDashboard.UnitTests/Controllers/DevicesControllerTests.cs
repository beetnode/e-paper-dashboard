using CSharpFunctionalExtensions;
using EPaperDashboard.Controllers;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.UnitTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Controllers;

public class DevicesControllerTests
{
    private readonly Mock<IDeviceRepository> _deviceRepository = new();
    private readonly Mock<IDashboardRepository> _dashboardRepository = new();

    private DevicesController CreateSut() => new(
        new DeviceService(_deviceRepository.Object),
        new DashboardService(_dashboardRepository.Object));

    [Fact]
    public void GetDevices_ReturnsDevicesForCurrentUser()
    {
        var userId = UserId.New();
        _deviceRepository.Setup(r => r.FindByUserId(userId)).Returns([new Device { UserId = userId, Name = "Device 1" }]);
        var sut = CreateSut().WithUser(userId);

        var result = sut.GetDevices();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<List<DeviceResponseDto>>().Which.Should().ContainSingle(d => d.Name == "Device 1");
    }

    [Fact]
    public void GetDevicesForDashboard_InvalidId_ReturnsBadRequest()
    {
        var sut = CreateSut().WithUser(UserId.New());

        var result = sut.GetDevicesForDashboard("not-valid");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void UpdateDevice_InvalidDeviceId_ReturnsBadRequest()
    {
        var sut = CreateSut().WithUser(UserId.New());

        var result = sut.UpdateDevice("bad-id", new UpdateDeviceRequest(null, null));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void UpdateDevice_NotFound_ReturnsNotFound()
    {
        var deviceId = DeviceId.New();
        _deviceRepository.Setup(r => r.FindById(deviceId)).Returns((Device?)null);
        var sut = CreateSut().WithUser(UserId.New());

        var result = sut.UpdateDevice(deviceId.Value, new UpdateDeviceRequest(null, null));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void UpdateDevice_OwnedByDifferentUser_ReturnsForbid()
    {
        var deviceId = DeviceId.New();
        var device = new Device { Id = deviceId, UserId = UserId.New() };
        _deviceRepository.Setup(r => r.FindById(deviceId)).Returns(device);
        var sut = CreateSut().WithUser(UserId.New());

        var result = sut.UpdateDevice(deviceId.Value, new UpdateDeviceRequest(null, null));

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public void UpdateDevice_EmptyDashboardIdString_UnassignsDashboard()
    {
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var device = new Device { Id = deviceId, UserId = userId, DashboardId = DashboardId.New() };
        _deviceRepository.Setup(r => r.FindById(deviceId)).Returns(device);
        var sut = CreateSut().WithUser(userId);

        var result = sut.UpdateDevice(deviceId.Value, new UpdateDeviceRequest(null, DashboardId: ""));

        result.Should().BeOfType<OkObjectResult>();
        device.DashboardId.Should().Be(DashboardId.Empty);
    }

    [Fact]
    public void UpdateDevice_AssignToNonExistentDashboard_ReturnsNotFound()
    {
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var targetDashboardId = DashboardId.New();
        var device = new Device { Id = deviceId, UserId = userId };
        _deviceRepository.Setup(r => r.FindById(deviceId)).Returns(device);
        _dashboardRepository.Setup(r => r.FindById(targetDashboardId)).Returns((Dashboard?)null);
        var sut = CreateSut().WithUser(userId);

        var result = sut.UpdateDevice(deviceId.Value, new UpdateDeviceRequest(null, targetDashboardId.Value));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void UpdateDevice_AssignToDashboardOwnedByAnotherUser_ReturnsForbid()
    {
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var targetDashboardId = DashboardId.New();
        var device = new Device { Id = deviceId, UserId = userId };
        var otherUsersDashboard = new Dashboard { Id = targetDashboardId, UserId = UserId.New() };
        _deviceRepository.Setup(r => r.FindById(deviceId)).Returns(device);
        _dashboardRepository.Setup(r => r.FindById(targetDashboardId)).Returns(otherUsersDashboard);
        var sut = CreateSut().WithUser(userId);

        var result = sut.UpdateDevice(deviceId.Value, new UpdateDeviceRequest(null, targetDashboardId.Value));

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public void UpdateDevice_ScreenSizeMismatchBetweenDeviceAndDashboard_ReturnsBadRequest()
    {
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var targetDashboardId = DashboardId.New();
        var device = new Device { Id = deviceId, UserId = userId, ScreenWidth = 800, ScreenHeight = 480 };
        var dashboard = new Dashboard { Id = targetDashboardId, UserId = userId, ScreenWidth = 480, ScreenHeight = 800 };
        _deviceRepository.Setup(r => r.FindById(deviceId)).Returns(device);
        _dashboardRepository.Setup(r => r.FindById(targetDashboardId)).Returns(dashboard);
        var sut = CreateSut().WithUser(userId);

        var result = sut.UpdateDevice(deviceId.Value, new UpdateDeviceRequest(null, targetDashboardId.Value));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void UpdateDevice_ValidDashboardAssignment_UpdatesDeviceAndReturnsDto()
    {
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var targetDashboardId = DashboardId.New();
        var device = new Device { Id = deviceId, UserId = userId, Name = "Kitchen" };
        var dashboard = new Dashboard { Id = targetDashboardId, UserId = userId };
        _deviceRepository.Setup(r => r.FindById(deviceId)).Returns(device);
        _dashboardRepository.Setup(r => r.FindById(targetDashboardId)).Returns(dashboard);
        var sut = CreateSut().WithUser(userId);

        var result = sut.UpdateDevice(deviceId.Value, new UpdateDeviceRequest("New Name", targetDashboardId.Value));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DeviceResponseDto>().Which.Name.Should().Be("New Name");
        device.DashboardId.Should().Be(targetDashboardId);
        _deviceRepository.Verify(r => r.Update(device), Times.Once);
    }

    [Fact]
    public void DeleteDevice_OwnedByCurrentUser_DeletesAndReturnsOk()
    {
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var device = new Device { Id = deviceId, UserId = userId };
        _deviceRepository.Setup(r => r.FindById(deviceId)).Returns(device);
        var sut = CreateSut().WithUser(userId);

        var result = sut.DeleteDevice(deviceId.Value);

        result.Should().BeOfType<OkResult>();
        _deviceRepository.Verify(r => r.Delete(deviceId), Times.Once);
    }
}
