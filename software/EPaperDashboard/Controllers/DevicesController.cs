using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Services;
using EPaperDashboard.Guards;
using EPaperDashboard.Models;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesController(DeviceService deviceService, DashboardService dashboardService) : BaseApiController
{
    private readonly DeviceService _deviceService = deviceService;
    private readonly DashboardService _dashboardService = dashboardService;

    [HttpGet]
    public IActionResult GetDevices()
    {
        var devices = _deviceService.GetDevicesForUser(CurrentUserId);
        var result = devices.Select(d => DeviceResponseDto.FromDevice(d)).ToList();
        return Ok(result);
    }

    [HttpGet("dashboard/{dashboardId}")]
    [DashboardOwner]
    public IActionResult GetDevicesForDashboard(string dashboardId)
    {
        if (!DashboardId.TryParse(dashboardId, out var id))
        {
            return BadRequest("Invalid dashboard ID");
        }

        var devices = _deviceService.GetDevicesForDashboard(id);
        var result = devices.Select(d => DeviceResponseDto.FromDevice(d)).ToList();
        return Ok(result);
    }

    [HttpPut("{deviceId}")]
    public IActionResult UpdateDevice(string deviceId, [FromBody] UpdateDeviceRequest request)
    {
        if (!DeviceId.TryParse(deviceId, out var id))
        {
            return BadRequest("Invalid device ID");
        }

        var device = _deviceService.GetDeviceById(id);
        if (device is null)
        {
            return NotFound("Device not found");
        }

        if (device.UserId != CurrentUserId)
        {
            return Forbid();
        }

        if (request.Name != null)
        {
            device.Name = request.Name;
        }

        if (request.DashboardId != null)
        {
            if (string.IsNullOrEmpty(request.DashboardId))
            {
                // Unassign dashboard
                device.DashboardId = DashboardId.Empty;
            }
            else if (DashboardId.TryParse(request.DashboardId, out var dashboardId))
            {
                // Verify the user owns the dashboard
                var dashboard = _dashboardService.GetDashboardById(dashboardId);
                if (dashboard is null)
                {
                    return NotFound("Dashboard not found");
                }
                if (dashboard.UserId != CurrentUserId)
                {
                    return Forbid();
                }
                if (device.ScreenWidth.HasValue && device.ScreenHeight.HasValue)
                {
                    if (device.ScreenWidth != dashboard.ScreenWidth
                        || device.ScreenHeight != dashboard.ScreenHeight)
                    {
                        return BadRequest($"Dashboard size ({dashboard.ScreenWidth}×{dashboard.ScreenHeight}) " +
                            $"does not match device screen size ({device.ScreenWidth}×{device.ScreenHeight}).");
                    }
                }
                device.DashboardId = dashboardId;
            }
            else
            {
                return BadRequest("Invalid dashboard ID");
            }
        }

        _deviceService.UpdateDevice(device);
        return Ok(DeviceResponseDto.FromDevice(device));
    }

    [HttpDelete("{deviceId}")]
    public IActionResult DeleteDevice(string deviceId)
    {
        if (!DeviceId.TryParse(deviceId, out var id))
        {
            return BadRequest("Invalid device ID");
        }

        var device = _deviceService.GetDeviceById(id);
        if (device is null)
        {
            return NotFound("Device not found");
        }

        if (device.UserId != CurrentUserId)
        {
            return Forbid();
        }

        _deviceService.DeleteDevice(id);
        return Ok();
    }
}

public record UpdateDeviceRequest(string? Name, string? DashboardId);

public record DeviceResponseDto(
    string Id,
    string DeviceIdentifier,
    string Name,
    string? DashboardId,
    string? DashboardName,
    DateTimeOffset PairedAt,
    DateTimeOffset? LastSeenAt,
    string? FirmwareVersion,
    int? ScreenWidth,
    int? ScreenHeight)
{
    public static DeviceResponseDto FromDevice(Device device, string? dashboardName = null) => new(
        Id: device.Id.ToString(),
        DeviceIdentifier: device.DeviceIdentifier,
        Name: device.Name,
        DashboardId: device.DashboardId == Models.DashboardId.Empty ? null : device.DashboardId.ToString(),
        DashboardName: dashboardName,
        PairedAt: device.PairedAt,
        LastSeenAt: device.LastSeenAt,
        FirmwareVersion: device.FirmwareVersion,
        ScreenWidth: device.ScreenWidth,
        ScreenHeight: device.ScreenHeight
    );
}
