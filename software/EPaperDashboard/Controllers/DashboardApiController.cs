using EPaperDashboard.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using EPaperDashboard.Services;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Plugins.HomeAssistant;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/dashboards")]
[Authorize]
public class DashboardApiController(DashboardService dashboardService, UserService userService,
    HomeAssistantDashboardTargetResolver homeAssistantTargets) : BaseApiController
{
    private readonly DashboardService _dashboardService = dashboardService;
    private readonly UserService _userService = userService;

    [HttpGet]
    public IActionResult GetDashboards()
    {
        if (!_userService.HasAccount(CurrentUserId))
        {
            return Unauthorized();
        }

        // Never return persisted dashboards directly: responses expose only the fields the client needs.
        var dashboards = _dashboardService.GetDashboardsForUser(CurrentUserId)
            .Select(dashboard => DashboardResponseDto.FromDashboard(dashboard, ResolveEffectiveAiConfigMode(dashboard)))
            .ToList();
        return Ok(dashboards);
    }

    [HttpGet("{id}")]
    public IActionResult GetDashboard(string id)
    {
        if (!DashboardId.TryParse(id, out var dashboardId))
        {
            return BadRequest(new { message = "Invalid dashboard ID." });
        }

        var dashboard = _dashboardService.GetDashboardById(dashboardId);
        if (dashboard.HasNoValue)
        {
            return NotFound(new { message = "Dashboard not found." });
        }

        if (dashboard.Value.UserId != CurrentUserId)
        {
            return Forbid();
        }

        return Ok(DashboardResponseDto.FromDashboard(dashboard.Value, ResolveEffectiveAiConfigMode(dashboard.Value)));
    }

    [HttpPost]
    public IActionResult CreateDashboard([FromBody] CreateDashboardRequest request)
    {
        if (!_userService.HasAccount(CurrentUserId))
        {
            return Unauthorized();
        }

        if (UserLimits.AppliesTo(CurrentUserId)
            && _dashboardService.GetDashboardsForUser(CurrentUserId).Count >= UserLimits.MaxDashboards)
        {
            return BadRequest(new { message = $"You can create up to {UserLimits.MaxDashboards} dashboards." });
        }

        var dashboard = new Dashboard
        {
            UserId = CurrentUserId,
            Name = request.Name,
            Description = request.Description ?? string.Empty
        };

        if (request.Orientation != null && Enum.TryParse<DashboardOrientation>(request.Orientation, out var orientation))
        {
            dashboard.Orientation = orientation;
        }

        if (request.ScreenWidth.HasValue && request.ScreenHeight.HasValue)
        {
            if (!DashboardSizePreset.IsValidSize(request.ScreenWidth.Value, request.ScreenHeight.Value))
            {
                return BadRequest(new { message = "Invalid dashboard size. Please select a supported size." });
            }
            var preset = DashboardSizePreset.FindByDimensions(request.ScreenWidth.Value, request.ScreenHeight.Value)!;
            dashboard.ScreenWidth = preset.Width;
            dashboard.ScreenHeight = preset.Height;
        }

        if (request.RenderingMode != null)
        {
            if (!Enum.TryParse<RenderingMode>(request.RenderingMode, out var renderingMode))
            {
                return BadRequest(new { message = "Invalid rendering mode." });
            }
            dashboard.RenderingMode = renderingMode;
        }

        if (request.Path != null) dashboard.Path = request.Path.Trim();

        var connectionError = ApplyHomeAssistantConnection(dashboard, request.HomeAssistantConnectionId);
        if (connectionError != null)
        {
            return BadRequest(new { message = connectionError });
        }

        _dashboardService.AddDashboard(dashboard);

        return Ok(DashboardResponseDto.FromDashboard(dashboard, ResolveEffectiveAiConfigMode(dashboard)));
    }

    [HttpPut("{id}")]
    public IActionResult UpdateDashboard(string id, [FromBody] UpdateDashboardRequest request)
    {
        if (!DashboardId.TryParse(id, out var dashboardId))
        {
            return BadRequest(new { message = "Invalid dashboard ID." });
        }

        var dashboard = _dashboardService.GetDashboardById(dashboardId);
        if (dashboard.HasNoValue)
        {
            return NotFound(new { message = "Dashboard not found." });
        }

        if (dashboard.Value.UserId != CurrentUserId)
        {
            return Forbid();
        }

        var updatedDashboard = dashboard.Value;
        if (request.Name != null) updatedDashboard.Name = request.Name;
        if (request.Description != null) updatedDashboard.Description = request.Description;

        if (request.Path != null) updatedDashboard.Path = request.Path;
        var connectionError = ApplyHomeAssistantConnection(updatedDashboard, request.HomeAssistantConnectionId);
        if (connectionError != null)
        {
            return BadRequest(new { message = connectionError });
        }
        if (request.UpdateTimes != null) updatedDashboard.UpdateTimes = request.UpdateTimes;
        if (request.LayoutConfig != null) updatedDashboard.LayoutConfig = request.LayoutConfig;
        
        // The dashboard type is chosen at creation; an update may only repeat it.
        if (request.RenderingMode != null
            && (!Enum.TryParse<RenderingMode>(request.RenderingMode, out var renderingMode) || renderingMode != updatedDashboard.RenderingMode))
        {
            return BadRequest(new { message = "The dashboard type cannot be changed after creation." });
        }

        if (request.Orientation != null)
        {
            if (Enum.TryParse<DashboardOrientation>(request.Orientation, out var orientation))
            {
                updatedDashboard.Orientation = orientation;
            }
        }

        if (request.ScreenWidth.HasValue && request.ScreenHeight.HasValue)
        {
            if (!DashboardSizePreset.IsValidSize(request.ScreenWidth.Value, request.ScreenHeight.Value))
            {
                return BadRequest(new { message = "Invalid dashboard size. Please select a supported size." });
            }
            var preset = DashboardSizePreset.FindByDimensions(request.ScreenWidth.Value, request.ScreenHeight.Value)!;
            updatedDashboard.ScreenWidth = preset.Width;
            updatedDashboard.ScreenHeight = preset.Height;
        }

        if (request.AiConfig != null)
        {
            if (!string.IsNullOrEmpty(request.AiConfig.HomeAssistantConnectionId)
                && homeAssistantTargets.FindConnection(request.AiConfig.HomeAssistantConnectionId, CurrentUserId) == null)
            {
                return BadRequest(new { message = "Home Assistant connection not found." });
            }
            updatedDashboard.AiConfig = request.AiConfig;
        }
        if (request.IsAiEnabled.HasValue) updatedDashboard.IsAiEnabled = request.IsAiEnabled.Value;
        if (request.AiPrompt != null) updatedDashboard.AiPrompt = request.AiPrompt;
        if (request.AiLeadTimeMinutes.HasValue) updatedDashboard.AiLeadTimeMinutes = request.AiLeadTimeMinutes.Value;

        // Auto-disable AI if no effective config is available
        if (updatedDashboard.IsAiEnabled && !HasEffectiveAiConfig(updatedDashboard))
        {
            updatedDashboard.IsAiEnabled = false;
        }

        _dashboardService.UpdateDashboard(updatedDashboard);

        return Ok(DashboardResponseDto.FromDashboard(updatedDashboard, ResolveEffectiveAiConfigMode(updatedDashboard)));
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteDashboard(string id)
    {
        if (!DashboardId.TryParse(id, out var dashboardId))
        {
            return BadRequest(new { message = "Invalid dashboard ID." });
        }

        var dashboard = _dashboardService.GetDashboardById(dashboardId);
        if (dashboard.HasNoValue)
        {
            return NotFound(new { message = "Dashboard not found." });
        }

        if (dashboard.Value.UserId != CurrentUserId)
        {
            return Forbid();
        }

        _dashboardService.DeleteDashboard(dashboardId);

        return Ok(new { message = "Dashboard deleted successfully." });
    }

    /// <summary>
    /// Null leaves the connection unchanged; an empty value removes it.
    /// </summary>
    private string? ApplyHomeAssistantConnection(Dashboard dashboard, string? connectionId)
    {
        if (connectionId == null)
        {
            return null;
        }

        if (connectionId.Length == 0)
        {
            dashboard.HomeAssistantConnectionId = null;
            return null;
        }

        var connection = homeAssistantTargets.FindConnection(connectionId, CurrentUserId);
        if (connection == null)
        {
            return "Home Assistant connection not found.";
        }

        if (!homeAssistantTargets.CanRender(connection))
        {
            return HomeAssistantDashboardTargetResolver.DefaultConnectionCannotRender;
        }

        dashboard.HomeAssistantConnectionId = connectionId;
        return null;
    }

    private bool HasEffectiveAiConfig(Dashboard dashboard)
    {
        return ResolveEffectiveAiConfigMode(dashboard) != AiConnectionMode.None.ToString();
    }

    private string ResolveEffectiveAiConfigMode(Dashboard dashboard)
    {
        if (dashboard.AiConfig != null && dashboard.AiConfig.ConnectionMode == AiConnectionMode.HomeAssistant)
        {
            return dashboard.AiConfig.ConnectionMode.ToString();
        }

        var globalConfig = _userService.GetAiConfig(dashboard.UserId);
        if (globalConfig != null && globalConfig.ConnectionMode != AiConnectionMode.None)
        {
            return globalConfig.ConnectionMode.ToString();
        }

        return AiConnectionMode.None.ToString();
    }
}

public record CreateDashboardRequest(
    string Name,
    string? Description,
    string? Orientation,
    int? ScreenWidth,
    int? ScreenHeight,
    string? RenderingMode = null,
    string? Path = null,
    string? HomeAssistantConnectionId = null);

public record UpdateDashboardRequest(
    string? Name,
    string? Description,
    string? Path,
    List<TimeOnly>? UpdateTimes,
    LayoutConfig? LayoutConfig,
    string? RenderingMode,
    string? Orientation,
    int? ScreenWidth,
    int? ScreenHeight,
    AiConfig? AiConfig,
    bool? IsAiEnabled,
    string? AiPrompt,
    int? AiLeadTimeMinutes,
    string? HomeAssistantConnectionId = null
);

public record DashboardResponseDto(
    string Id,
    string Name,
    string Description,
    string UserId,
    string? Path,
    string? HomeAssistantConnectionId,
    List<TimeOnly>? UpdateTimes,
    LayoutConfig? LayoutConfig,
    string? RenderingMode,
    string Orientation,
    int ScreenWidth,
    int ScreenHeight,
    AiConfig? AiConfig,
    bool IsAiEnabled,
    string? AiPrompt,
    int AiLeadTimeMinutes,
    DateTimeOffset? LastAiGenerationTime,
    string? LastAiGenerationError,
    string EffectiveAiConfigMode
)
{
    public static DashboardResponseDto FromDashboard(Dashboard dashboard, string? effectiveAiConfigMode = null) => new(
        Id: dashboard.Id.ToString(),
        Name: dashboard.Name,
        Description: dashboard.Description,
        UserId: dashboard.UserId.ToString(),
        Path: dashboard.Path,
        HomeAssistantConnectionId: dashboard.HomeAssistantConnectionId,
        UpdateTimes: dashboard.UpdateTimes,
        LayoutConfig: dashboard.LayoutConfig,
        RenderingMode: dashboard.RenderingMode.ToString(),
        Orientation: dashboard.Orientation.ToString(),
        ScreenWidth: dashboard.ScreenWidth,
        ScreenHeight: dashboard.ScreenHeight,
        AiConfig: dashboard.AiConfig,
        IsAiEnabled: dashboard.IsAiEnabled,
        AiPrompt: dashboard.AiPrompt,
        AiLeadTimeMinutes: dashboard.AiLeadTimeMinutes,
        LastAiGenerationTime: dashboard.LastAiGenerationTime,
        LastAiGenerationError: dashboard.LastAiGenerationError,
        EffectiveAiConfigMode: effectiveAiConfigMode ?? AiConnectionMode.None.ToString()
    );
}
