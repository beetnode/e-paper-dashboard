using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Plugins.HomeAssistant;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/homeassistant")]
[Authorize]
public class HomeAssistantController(
    HomeAssistantService homeAssistantService,
    HomeAssistantDashboardTargetResolver homeAssistantTargets) : BaseApiController
{
    /// <summary>Lists the dashboards of a Home Assistant connection, to pick the one a dashboard renders.</summary>
    [HttpGet("connections/{connectionId}/dashboards")]
    public async Task<IActionResult> FetchConnectionDashboards(string connectionId)
    {
        var connection = homeAssistantTargets.FindConnection(connectionId, CurrentUserId);
        if (connection is null)
            return NotFound(new { error = "Home Assistant connection not found." });

        var result = await homeAssistantService.FetchDashboards(connection);

        return result.IsSuccess
            ? Ok(new { data = result.Value })
            : BadRequest(new { error = result.Error });
    }
}
