using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Utilities;
using EPaperDashboard.Services;
using EPaperDashboard.Models;
using System.Text;
using EPaperDashboard.Guards;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/configuration")]
[Authorize(Policy = "ApiKeyPolicy")]
[DeviceAccessible]
public class ConfigurationApiController(DashboardService dashboardService, DeviceService deviceService) : ControllerBase
{
    private readonly DashboardService _dashboardService = dashboardService;
    private readonly DeviceService _deviceService = deviceService;

    private const int GracefulPeriodSeconds = 120;

    [HttpGet("next-update-wait-seconds")]
    public IActionResult GetNextUpdateWait([FromHeader(Name = HttpHeaderNames.ApiKeyHeaderName)] string apiKey)
    {
        var now = DateTime.Now;
        var dashboard = ResolveDashboardByApiKey(apiKey);
        return GetNextUpdateTime(now, dashboard?.UpdateTimes) is { } nextUpdate
            ? Content(((long)(nextUpdate - now).TotalSeconds).ToString(), "text/plain", Encoding.UTF8)
            : NotFound("No upcoming update times found.");

        DateTime? GetNextUpdateTime(DateTime now, List<TimeOnly>? updateTimes)
        {
            if (updateTimes is null || !updateTimes.Any())
            {
                return null;
            }

            var today = now.Date;
            var tomorrow = today.AddDays(1);
            var times = updateTimes.OrderBy(t => t).ToList();

            var gracefulCutoff = now.AddSeconds(GracefulPeriodSeconds);
            return times
                .Select(t => today.Add(t.ToTimeSpan()))
                .Append(tomorrow.Add(times.First().ToTimeSpan()))
                .Where(dt => dt > gracefulCutoff)
                .Select(dt => (DateTime?)dt)
                .FirstOrDefault();
        }
    }

    private Dashboard? ResolveDashboardByApiKey(string apiKey)
    {
        var device = _deviceService.GetDeviceByApiKey(apiKey);
        if (device is not null && device.DashboardId != DashboardId.Empty)
        {
            return _dashboardService.GetDashboardById(device.DashboardId);
        }

        return null;
    }
}
