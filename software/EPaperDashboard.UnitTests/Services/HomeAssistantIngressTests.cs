using System.Net;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class HomeAssistantIngressTests
{
    private const int DevicePort = 8129;

    private static DefaultHttpContext Request(string? ingressPath = "/api/hassio_ingress/abc123", string? remoteAddress = "172.30.32.2", int localPort = 8128)
    {
        var context = new DefaultHttpContext();
        if (ingressPath is not null)
        {
            context.Request.Headers[Constants.IngressPathHeader] = ingressPath;
        }
        context.Connection.RemoteIpAddress = remoteAddress is null ? null : IPAddress.Parse(remoteAddress);
        context.Connection.LocalPort = localPort;
        return context;
    }

    [Fact]
    public void IsTrusted_SupervisorRequest_ReturnsTrue() =>
        HomeAssistantIngress.IsTrusted(Request(), DevicePort, allowLoopback: false).Should().BeTrue();

    [Fact]
    public void IsTrusted_SupervisorAddressMappedToIPv6_ReturnsTrue() =>
        HomeAssistantIngress.IsTrusted(Request(remoteAddress: "::ffff:172.30.32.2"), DevicePort, allowLoopback: false).Should().BeTrue();

    [Fact]
    public void IsTrusted_WithoutIngressHeader_ReturnsFalse() =>
        HomeAssistantIngress.IsTrusted(Request(ingressPath: null), DevicePort, allowLoopback: false).Should().BeFalse();

    [Theory]
    [InlineData("172.30.32.1")]
    [InlineData("192.168.1.20")]
    [InlineData("127.0.0.1")]
    public void IsTrusted_OtherAddress_ReturnsFalse(string remoteAddress) =>
        HomeAssistantIngress.IsTrusted(Request(remoteAddress: remoteAddress), DevicePort, allowLoopback: false).Should().BeFalse();

    [Fact]
    public void IsTrusted_UnknownAddress_ReturnsFalse() =>
        HomeAssistantIngress.IsTrusted(Request(remoteAddress: null), DevicePort, allowLoopback: false).Should().BeFalse();

    [Fact]
    public void IsTrusted_LoopbackWhenAllowed_ReturnsTrue() =>
        HomeAssistantIngress.IsTrusted(Request(remoteAddress: "127.0.0.1"), DevicePort, allowLoopback: true).Should().BeTrue();

    [Fact]
    public void IsTrusted_OnDevicePort_ReturnsFalse() =>
        HomeAssistantIngress.IsTrusted(Request(localPort: DevicePort), DevicePort, allowLoopback: true).Should().BeFalse();

    [Theory]
    [InlineData("/api/hassio_ingress/abc123", "/api/hassio_ingress/abc123")]
    [InlineData("/api/hassio_ingress/abc-1_2/", "/api/hassio_ingress/abc-1_2")]
    [InlineData("api/hassio_ingress/abc123", "/api/hassio_ingress/abc123")]
    public void TryGetPath_SupervisorFormat_ReturnsNormalizedPath(string header, string expected)
    {
        HomeAssistantIngress.TryGetPath(Request(header), out var path).Should().BeTrue();
        path.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/ingress/x")]
    [InlineData("/api/hassio_ingress/abc/extra")]
    [InlineData("/api/hassio_ingress/abc\"><script>alert(1)</script>")]
    public void TryGetPath_OtherValues_ReturnsFalse(string header) =>
        HomeAssistantIngress.TryGetPath(Request(header), out _).Should().BeFalse();
}
