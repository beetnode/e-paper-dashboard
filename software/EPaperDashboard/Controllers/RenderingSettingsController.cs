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
        return Ok(new { enabled, component = new
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
            if (request.Enabled) manager.StartInstallation(onlyIfNeeded: true);
            access.SetEnabled(CurrentUserId, request.Enabled);
            return await GetStatus();
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
