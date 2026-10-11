using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Providers;
using EPaperDashboard.Guards;
using EPaperDashboard.Services.Plugins.HomeAssistant;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/homeassistant")]
[Authorize]
public class HomeAssistantController(
    HomeAssistantService homeAssistantService,
    IEntityStateProvider entityStateProvider,
    IEntityHistoryProvider entityHistoryProvider,
    HomeAssistantDashboardTargetResolver homeAssistantTargets,
    ILogger<HomeAssistantController> logger) : BaseApiController
{
    private readonly HomeAssistantService _homeAssistantService = homeAssistantService;
    private readonly IEntityStateProvider _entityStateProvider = entityStateProvider;
    private readonly IEntityHistoryProvider _entityHistoryProvider = entityHistoryProvider;
    private readonly ILogger<HomeAssistantController> _logger = logger;

    [HttpPost("fetch-dashboards")]
    [DashboardOwnerFromBody]
    public async Task<IActionResult> FetchDashboards([FromBody] FetchDashboardsRequest request)
    {
        var result = await _homeAssistantService.FetchDashboards(request.DashboardId);

        return result.IsSuccess
            ? Ok(new { dashboards = result.Value })
            : BadRequest(new { error = result.Error });
    }

    [HttpGet("connections/{connectionId}/dashboards")]
    public async Task<IActionResult> FetchConnectionDashboards(string connectionId)
    {
        var connection = homeAssistantTargets.FindConnection(connectionId, CurrentUserId);
        if (connection is null)
            return NotFound(new { error = "Home Assistant connection not found." });

        var (host, token) = homeAssistantTargets.ConnectionServer(connection);
        var result = await _homeAssistantService.FetchConnectionDashboards(host, token);

        return result.IsSuccess
            ? Ok(new { data = result.Value })
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("fetch-entities")]
    [DashboardOwnerFromBody]
    public async Task<IActionResult> FetchEntities([FromBody] FetchEntitiesRequest request)
    {
        var result = await _homeAssistantService.FetchEntities(request.DashboardId);

        return result.IsSuccess
            ? Ok(new { entities = result.Value })
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("fetch-entity-states")]
    [DashboardOwnerFromBody]
    public async Task<IActionResult> FetchEntityStates([FromBody] FetchEntityStatesRequest request)
    {
        var result = await _entityStateProvider.FetchEntityStatesAsync(request.DashboardId, request.EntityIds ?? [], HttpContext.RequestAborted);

        return result.IsSuccess
            ? Ok(new { states = result.Value })
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("fetch-entity-history")]
    [DashboardOwnerFromBody]
    public async Task<IActionResult> FetchEntityHistory([FromBody] FetchEntityHistoryRequest request)
    {
        var hours = request.Hours ?? 24;
        if (hours < 1)
            hours = 1;
        if (hours > 720)
            hours = 720;

        var result = await _entityHistoryProvider.FetchEntityHistoryAsync(request.DashboardId, request.EntityIds ?? [], hours, HttpContext.RequestAborted);

        return result.IsSuccess
            ? Ok(new { history = result.Value })
            : BadRequest(new { error = result.Error });
    }

    public record FetchDashboardsRequest(string DashboardId);
    public record FetchEntitiesRequest(string DashboardId);
    public record FetchEntityStatesRequest(string DashboardId, string[] EntityIds);
    public record FetchEntityHistoryRequest(string DashboardId, string[] EntityIds, int? Hours = 24);
}
