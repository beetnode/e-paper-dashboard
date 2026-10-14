using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Services.Firmware;
using EPaperDashboard.Guards;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/firmware")]
public class FirmwareApiController(FirmwareUpdateService firmwareUpdateService) : ControllerBase
{
    /// <summary>
    /// Gets the latest available firmware release information.
    /// Used by the frontend UI to display firmware status.
    /// </summary>
    [HttpGet("latest")]
    [Authorize]
    public IActionResult GetLatestFirmware()
    {
        var release = firmwareUpdateService.GetLatestRelease();
        if (release is null)
            return Ok(new
            {
                version = (string?)null,
                isUpdateAvailable = false,
                message = "No firmware release information available. The firmware update service may still be initializing."
            });

        return Ok(new
        {
            version = release.Version,
            releaseNotes = release.ReleaseNotes,
            publishedAt = release.PublishedAt,
            fileSize = release.FileSize,
            hasDownload = release.DownloadUrl is not null,
            isUpdateAvailable = firmwareUpdateService.IsOtaReady
        });
    }

    /// <summary>
    /// Forces an immediate check for firmware updates.
    /// Returns the latest firmware information after the check.
    /// </summary>
    [HttpPost("refresh")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> RefreshFirmwareCheck()
    {
        var release = await firmwareUpdateService.RefreshAsync(HttpContext.RequestAborted);
        if (release is null)
            return Ok(new
            {
                version = (string?)null,
                isUpdateAvailable = false,
                message = "No firmware release information available from the configured provider."
            });

        return Ok(new
        {
            version = release.Version,
            releaseNotes = release.ReleaseNotes,
            publishedAt = release.PublishedAt,
            fileSize = release.FileSize,
            hasDownload = release.DownloadUrl is not null,
            isUpdateAvailable = firmwareUpdateService.IsOtaReady
        });
    }

    /// <summary>
    /// Downloads the latest firmware binary for OTA updates.
    /// This endpoint is device-accessible (via device port with API key authentication).
    /// </summary>
    [HttpGet("download")]
    [Authorize(Policy = "ApiKeyPolicy")]
    [DeviceAccessible]
    public async Task<IActionResult> DownloadFirmware()
    {
        var path = await firmwareUpdateService.GetFirmwareBinaryPathAsync(HttpContext.RequestAborted);
        if (path is null)
            return NotFound("No firmware binary available for download.");

        var release = firmwareUpdateService.GetLatestRelease();
        var fileName = $"firmware-{release?.Version ?? "unknown"}.bin";

        return PhysicalFile(path, "application/octet-stream", fileName);
    }
}
