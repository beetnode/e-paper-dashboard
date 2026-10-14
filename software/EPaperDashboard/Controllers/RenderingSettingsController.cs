using EPaperDashboard.Services.Components.Playwright;
using EPaperDashboard.Services.Rendering;
using EPaperDashboard.Models.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/rendering")]
[Authorize]
public sealed class RenderingSettingsController(
    RenderingAccessService access, PlaywrightComponentManager manager,
    PlaywrightComponentRuntime runtime, IPageToImageRenderingService rendering) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetStatus()
    {
        var enabled = access.IsEnabled(CurrentUserId);
        var status = manager.GetStatus();
        return Ok(new { enabled, allowed = access.IsAllowed(CurrentUserId), canManage = IsSuperUser, component = new
        {
            status.State,
            status.Supported,
            Ready = status.State == PlaywrightComponentState.Installed && await runtime.IsAvailableAsync(HttpContext.RequestAborted)
        } });
    }

    [HttpPut]
    public async Task<IActionResult> SetEnabled([FromBody] RenderingSetting request)
    {
        try
        {
            access.RequireAccount(CurrentUserId);
            // Unsupported installations must not persist an enabled setting.
            if (request.Enabled && !manager.GetStatus().Supported)
                throw new PlatformNotSupportedException();
            // The component is shared by every account, so only an administrator may download it.
            if (request.Enabled && IsSuperUser) manager.StartInstallation(onlyIfNeeded: true);
            access.SetEnabled(CurrentUserId, request.Enabled);
            return await GetStatus();
        }
        catch (RenderingRequestException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or InvalidOperationException)
        {
            return BadRequest(new { message = exception is PlatformNotSupportedException
                ? "Browser rendering is unavailable on this server."
                : "Could not update rendering settings. Reload and try again." });
        }
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test()
    {
        var result = await rendering.RenderHtmlAsync(CurrentUserId,
            "<!doctype html><html><body>izBoard</body></html>", new Size(200, 100), HttpContext.RequestAborted);
        if (result.IsFailure) return BadRequest(new { message = "Rendering test failed. Check Rendering settings and try again." });
        using var image = result.Value;
        return Ok(new { message = "Rendering is ready." });
    }
}

public sealed record RenderingSetting(bool Enabled);
