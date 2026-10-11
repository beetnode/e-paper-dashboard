using EPaperDashboard.RenderingComponent;
using EPaperDashboard.Services.Components.Playwright;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/system/components/rendering")]
[Authorize]
public sealed class PlaywrightComponentController(
    PlaywrightComponentManager componentManager,
    PlaywrightComponentRuntime runtime) : BaseApiController
{
    [HttpGet]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<PlaywrightComponentStatus>> GetStatus()
    {
        var component = componentManager.GetActiveComponent();
        var status = componentManager.GetStatus();
        var ready = component is not null && await runtime.IsAvailableAsync(HttpContext.RequestAborted);
        return Ok(status with
        {
            Ready = ready,
            Error = status.State == PlaywrightComponentState.Installed && !ready
                ? "The rendering host is unavailable or does not match the application build. Check deployment configuration."
                : status.Error
        });
    }

    [HttpPost("install")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult Install()
    {
        try
        {
            return componentManager.StartInstallation()
                ? Accepted(componentManager.GetStatus())
                : Conflict(new { message = "The rendering component is already being installed." });
        }
        catch (PlatformNotSupportedException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("test")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> Test()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(150));
        try
        {
            using var lease = await runtime.AcquireRenderAsync(timeout.Token);
            var component = componentManager.GetActiveComponent();
            if (component is null) return Conflict(new { message = "The rendering component is not ready." });
            await runtime.RunAsync(component, new RenderRequest("html", 200, 100,
                Html: "<!doctype html><html><body>izBoard</body></html>"), timeout.Token);
            return Ok(new { message = "Rendering component tested successfully." });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(503, new { message = "The rendering component test failed. Check the renderer logs." });
        }
    }

    [HttpDelete]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> Uninstall()
    {
        try
        {
            await componentManager.UninstallAsync();
            return Ok(componentManager.GetStatus());
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
