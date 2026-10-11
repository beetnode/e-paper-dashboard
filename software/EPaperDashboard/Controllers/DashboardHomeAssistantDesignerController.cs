using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Services;
using EPaperDashboard.Guards;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/dashboards/{dashboardId}/homeassistant/designer")]
[Authorize]
[DashboardOwner]
public class DashboardHomeAssistantDesignerController(
    HomeAssistantService homeAssistantService) : ControllerBase
{
    private readonly HomeAssistantService _homeAssistantService = homeAssistantService;

    [HttpGet("entity-metadata")]
    public async Task<IActionResult> GetEntityMetadata(string dashboardId)
    {
        var result = await _homeAssistantService.FetchEntities(new HomeAssistantScope.DashboardSettings(dashboardId));
        return result.IsSuccess
            ? Ok(new { data = result.Value })
            : BadRequest(new { error = result.Error });
    }
}
