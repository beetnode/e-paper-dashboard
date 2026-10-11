using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Services;
using EPaperDashboard.Guards;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/dashboards/{dashboardId}/homeassistant")]
[Authorize]
[DashboardOwner]
public class DashboardHomeAssistantController(
    HomeAssistantService homeAssistantService) : ControllerBase
{
    private readonly HomeAssistantService _homeAssistantService = homeAssistantService;

    [HttpGet("dashboards")]
    public async Task<IActionResult> GetDashboards(string dashboardId)
    {
        var result = await _homeAssistantService.FetchDashboards(new HomeAssistantScope.DashboardSettings(dashboardId));
        return result.IsSuccess
            ? Ok(new { data = result.Value })
            : BadRequest(new { error = result.Error });
    }

    [HttpGet("entities")]
    public async Task<IActionResult> GetEntities(string dashboardId)
    {
        var result = await _homeAssistantService.FetchEntities(new HomeAssistantScope.DashboardSettings(dashboardId));
        return result.IsSuccess
            ? Ok(new { data = result.Value })
            : BadRequest(new { error = result.Error });
    }
}
