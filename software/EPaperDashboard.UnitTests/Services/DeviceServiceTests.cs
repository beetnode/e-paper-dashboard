using EPaperDashboard.Utilities;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class DeviceServiceTests
{
    [Fact]
    public void GetDeviceByApiKey_UnknownKey_ReturnsNull()
    {
        var repo = new Mock<IDeviceRepository>();
        repo.Setup(r => r.FindByApiKey("nope")).Returns((Device?)null);
        var sut = new DeviceService(repo.Object);

        var result = sut.GetDeviceByApiKey("nope");

        result.Should().BeNull();
    }

    [Fact]
    public void GetDeviceByApiKey_KnownKey_ReturnsDevice()
    {
        var device = new Device { Id = DeviceId.New() };
        var repo = new Mock<IDeviceRepository>();
        repo.Setup(r => r.FindByApiKey("key123")).Returns(device);
        var sut = new DeviceService(repo.Object);

        var result = sut.GetDeviceByApiKey("key123");

        result.Should().NotBeNull();
        result.Should().BeSameAs(device);
    }

    [Fact]
    public void GetDeviceByIdentifier_CallsRepositoryWithGivenIdentifier()
    {
        var repo = new Mock<IDeviceRepository>();
        repo.Setup(r => r.FindByIdentifier("dev-1")).Returns((Device?)null);
        var sut = new DeviceService(repo.Object);

        sut.GetDeviceByIdentifier("dev-1");

        repo.Verify(r => r.FindByIdentifier("dev-1"), Times.Once);
    }

    [Fact]
    public void UpdateDevice_UpdatesRepository()
    {
        var device = new Device { Id = DeviceId.New() };
        var repo = new Mock<IDeviceRepository>();
        var sut = new DeviceService(repo.Object);

        sut.UpdateDevice(device);

        repo.Verify(r => r.Update(device), Times.Once);
    }

    [Fact]
    public void DeleteDevice_DeletesFromRepositoryById()
    {
        var id = DeviceId.New();
        var repo = new Mock<IDeviceRepository>();
        var sut = new DeviceService(repo.Object);

        sut.DeleteDevice(id);

        repo.Verify(r => r.Delete(id), Times.Once);
    }

    [Fact]
    public void GetDeviceByApiKey_OwnerSuspended_ReturnsNone()
    {
        var owner = new User { Id = UserId.New(), IsSuspended = true };
        var repo = new Mock<IDeviceRepository>();
        repo.Setup(r => r.FindByApiKey("key")).Returns(new Device { UserId = owner.Id });
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.FindById(owner.Id)).Returns(owner);

        new DeviceService(repo.Object, users.Object).GetDeviceByApiKey("key").Should().BeNull();
    }

    [Fact]
    public void GetDeviceByApiKey_OwnerWithoutAccountRecord_ReturnsDevice()
    {
        var repo = new Mock<IDeviceRepository>();
        repo.Setup(r => r.FindByApiKey("key")).Returns(new Device { UserId = UserId.New() });
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.FindById(It.IsAny<UserId>())).Returns((User?)null);

        new DeviceService(repo.Object, users.Object).GetDeviceByApiKey("key").Should().NotBeNull();
    }
}
