using EPaperDashboard.Controllers;
using EPaperDashboard.Services.Firmware;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Controllers;

public sealed class FirmwareApiControllerTests : IDisposable
{
    private const string DownloadUrl = "https://example.test/firmware.bin";

    private readonly string _configDir = Path.Combine(Path.GetTempPath(), $"fw-api-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_configDir))
            Directory.Delete(_configDir, recursive: true);
    }

    [Fact]
    public void RefreshFirmwareCheck_RequiresSuperUser() =>
        Policies(nameof(FirmwareApiController.RefreshFirmwareCheck)).Should().Contain("SuperUserOnly");

    [Fact]
    public void GetLatestFirmware_IsAvailableToAnySignedInUser() =>
        Policies(nameof(FirmwareApiController.GetLatestFirmware)).Should().ContainSingle().Which.Should().BeNull();

    [Fact]
    public async Task DownloadFirmware_BinaryReady_SetsMd5Header()
    {
        var service = CreateService(binary: [1, 2, 3]);
        await service.RefreshAsync();
        var (controller, httpContext) = CreateController(service);

        var result = await controller.DownloadFirmware();

        result.Should().BeOfType<PhysicalFileResult>();
        httpContext.Response.Headers[HttpHeaderNames.FirmwareMd5HeaderName].ToString()
            .Should().Be("5289df737df57326fcdd22597afb1fac");
    }

    [Fact]
    public async Task DownloadFirmware_NoBinary_NotFoundWithoutMd5Header()
    {
        var service = CreateService(binary: null);
        var (controller, httpContext) = CreateController(service);

        var result = await controller.DownloadFirmware();

        result.Should().BeOfType<NotFoundObjectResult>();
        httpContext.Response.Headers.ContainsKey(HttpHeaderNames.FirmwareMd5HeaderName).Should().BeFalse();
    }

    private FirmwareUpdateService CreateService(byte[]? binary)
    {
        var provider = new Mock<IFirmwareReleaseProvider>();
        provider.Setup(p => p.GetLatestReleaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirmwareReleaseInfo("0.5.0", null, null, DownloadUrl, null));
        provider.Setup(p => p.DownloadFirmwareBinaryAsync(DownloadUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binary);

        var config = new Mock<IEnvironmentConfiguration>();
        config.SetupGet(c => c.ConfigDir).Returns(_configDir);
        config.SetupGet(c => c.FirmwareCheckInterval).Returns(TimeSpan.FromHours(6));

        return new FirmwareUpdateService(provider.Object, config.Object, NullLogger<FirmwareUpdateService>.Instance);
    }

    private static (FirmwareApiController Controller, HttpContext HttpContext) CreateController(FirmwareUpdateService service)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new FirmwareApiController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        return (controller, httpContext);
    }

    private static IEnumerable<string?> Policies(string methodName) =>
        typeof(FirmwareApiController).GetMethod(methodName)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Select(attribute => attribute.Policy);
}
