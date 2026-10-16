using EPaperDashboard.Guards;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EPaperDashboard.UnitTests.Guards;

public class DeviceAccessGuardTests
{
    private static Endpoint EndpointWith(params object[] metadata) =>
        new(null, new EndpointMetadataCollection(metadata), "test");

    [Fact]
    public void IsAccessible_NullEndpoint_ReturnsFalse()
    {
        DeviceAccessGuard.IsAccessible(null).Should().BeFalse();
    }

    [Fact]
    public void IsAccessible_NoDeviceAccessibleAttribute_ReturnsFalse()
    {
        var endpoint = EndpointWith();

        DeviceAccessGuard.IsAccessible(endpoint).Should().BeFalse();
    }

    [Fact]
    public void IsAccessible_DeviceAccessibleAttributePresent_ReturnsTrue()
    {
        var endpoint = EndpointWith(new DeviceAccessibleAttribute());

        DeviceAccessGuard.IsAccessible(endpoint).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void IsServedOnPort_DeviceEndpointsOnlyOnDevicePortOthersOnlyOnWebPort(
        bool deviceEndpoint, bool isDevicePort, bool expected)
    {
        var endpoint = deviceEndpoint ? EndpointWith(new DeviceAccessibleAttribute()) : EndpointWith();

        DeviceAccessGuard.IsServedOnPort(endpoint, isDevicePort).Should().Be(expected);
    }

    [Fact]
    public void IsServedOnPort_NoEndpoint_OnlyOnWebPort()
    {
        DeviceAccessGuard.IsServedOnPort(null, isDevicePort: false).Should().BeTrue();
        DeviceAccessGuard.IsServedOnPort(null, isDevicePort: true).Should().BeFalse();
    }
}
