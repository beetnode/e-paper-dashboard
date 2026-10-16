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
    private const string SignatureUrl = "https://example.test/firmware.bin.sig";

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
    public async Task DownloadFirmware_SignedRelease_SetsSha256AndSignatureHeaders()
    {
        var service = CreateService(binary: [1, 2, 3], signature: [0x30, 0x44]);
        await service.RefreshAsync();
        var (controller, httpContext) = CreateController(service);

        await controller.DownloadFirmware();

        httpContext.Response.Headers[HttpHeaderNames.FirmwareSha256HeaderName].ToString()
            .Should().Be("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81");
        httpContext.Response.Headers[HttpHeaderNames.FirmwareSignatureHeaderName].ToString()
            .Should().Be(Convert.ToBase64String([0x30, 0x44]));
    }

    [Fact]
    public async Task DownloadFirmware_UnsignedRelease_OmitsSignatureHeader()
    {
        var service = CreateService(binary: [1, 2, 3]);
        await service.RefreshAsync();
        var (controller, httpContext) = CreateController(service);

        await controller.DownloadFirmware();

        httpContext.Response.Headers.ContainsKey(HttpHeaderNames.FirmwareSignatureHeaderName).Should().BeFalse();
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

    private FirmwareUpdateService CreateService(byte[]? binary, byte[]? signature = null)
    {
        var provider = new Mock<IFirmwareReleaseProvider>();
        provider.Setup(p => p.GetLatestReleaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirmwareReleaseInfo("0.5.0", null, null, DownloadUrl, null,
                signature is null ? null : SignatureUrl));
        provider.Setup(p => p.DownloadFirmwareBinaryAsync(DownloadUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binary);
        provider.Setup(p => p.DownloadFirmwareBinaryAsync(SignatureUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(signature);

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
