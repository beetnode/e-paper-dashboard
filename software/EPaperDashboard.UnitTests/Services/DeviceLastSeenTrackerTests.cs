using EPaperDashboard.Utilities;
using EPaperDashboard.Models;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Services;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class DeviceLastSeenTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 17, 8, 0, 0, TimeSpan.Zero);

    private static DeviceLastSeenTracker CreateSut() => new(new FakeTimeProvider(Now));

    [Fact]
    public void ShouldUpdate_FirmwareVersionChanged_ReturnsTrue()
    {
        var device = new Device { FirmwareVersion = "1.0.0", LastSeenAt = Now };
        var sut = CreateSut();

        sut.ShouldUpdate(device, "1.1.0").Should().BeTrue();
    }

    [Fact]
    public void ShouldUpdate_NeverSeenBefore_ReturnsTrue()
    {
        var device = new Device { FirmwareVersion = "1.0.0", LastSeenAt = null };
        var sut = CreateSut();

        sut.ShouldUpdate(device, "1.0.0").Should().BeTrue();
    }

    [Fact]
    public void ShouldUpdate_LastSeenOverOneMinuteAgo_ReturnsTrue()
    {
        var device = new Device { FirmwareVersion = "1.0.0", LastSeenAt = Now.AddMinutes(-1).AddTicks(-1) };
        var sut = CreateSut();

        sut.ShouldUpdate(device, "1.0.0").Should().BeTrue();
    }

    [Fact]
    public void ShouldUpdate_LastSeenExactlyOneMinuteAgo_ReturnsFalse()
    {
        var device = new Device { FirmwareVersion = "1.0.0", LastSeenAt = Now.AddMinutes(-1) };
        var sut = CreateSut();

        sut.ShouldUpdate(device, "1.0.0").Should().BeFalse();
    }

    [Fact]
    public void ShouldUpdate_SameFirmwareAndRecentlySeen_ReturnsFalse()
    {
        var device = new Device { FirmwareVersion = "1.0.0", LastSeenAt = Now.AddSeconds(-30) };
        var sut = CreateSut();

        sut.ShouldUpdate(device, "1.0.0").Should().BeFalse();
    }

    [Fact]
    public void Apply_SetsFirmwareVersionAndLastSeenAtToCurrentTime()
    {
        var device = new Device { FirmwareVersion = "1.0.0", LastSeenAt = null };
        var sut = CreateSut();

        sut.Apply(device, "2.0.0");

        device.FirmwareVersion.Should().Be("2.0.0");
        device.LastSeenAt.Should().Be(Now);
    }

    [Fact]
    public void Record_ValidApiKey_UpdatesThatDevice()
    {
        var device = new Device { Id = DeviceId.New(), FirmwareVersion = "0.7.0" };
        var repo = new Mock<IDeviceRepository>();
        repo.Setup(r => r.FindByApiKeyHash(DeviceApiKey.Hash("good-key"))).Returns(device);

        CreateSut().Record(new DeviceService(repo.Object), "good-key", "0.8.0");

        device.FirmwareVersion.Should().Be("0.8.0");
        device.LastSeenAt.Should().Be(Now);
        repo.Verify(r => r.Update(device), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-key")]
    public void Record_WithoutValidApiKey_UpdatesNothing(string? apiKey)
    {
        var repo = new Mock<IDeviceRepository>();
        repo.Setup(r => r.FindByApiKeyHash(It.IsAny<string>())).Returns((Device?)null);
        repo.Setup(r => r.FindByIdentifier(It.IsAny<string>())).Returns(new Device { Id = DeviceId.New() });

        CreateSut().Record(new DeviceService(repo.Object), apiKey, "0.8.0");

        repo.Verify(r => r.Update(It.IsAny<Device>()), Times.Never);
    }

    [Fact]
    public void Record_RecentlySeenWithSameFirmware_SkipsWrite()
    {
        var device = new Device { Id = DeviceId.New(), FirmwareVersion = "0.8.0", LastSeenAt = Now };
        var repo = new Mock<IDeviceRepository>();
        repo.Setup(r => r.FindByApiKeyHash(DeviceApiKey.Hash("good-key"))).Returns(device);

        CreateSut().Record(new DeviceService(repo.Object), "good-key", "0.8.0");

        repo.Verify(r => r.Update(It.IsAny<Device>()), Times.Never);
    }
}
