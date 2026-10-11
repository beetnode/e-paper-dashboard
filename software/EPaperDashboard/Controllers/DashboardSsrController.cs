using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Rendering;
using EPaperDashboard.Models;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.Utilities;
using CSharpFunctionalExtensions;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

using EPaperDashboard.Services.Plugins.HomeAssistant;

namespace EPaperDashboard.Controllers;

/// <summary>
/// Serves dashboards as server-side rendered images.
/// Uses the same cookie auth as the Angular frontend.
/// </summary>
[ApiController]
[Route("api/dashboards")]
[Authorize]
public class DashboardSsrController(
    DashboardService dashboardService,
    DashboardImageRenderingService dashboardImageRenderingService,
    DesignerPreviewImageStore designerPreviewImageStore,
    IPageToImageRenderingService renderingService,
    HomeAssistantDashboardTargetResolver homeAssistantTargets,
    TimeProvider timeProvider) : BaseApiController
{
    /// <summary>
    /// Returns the dashboard rendered directly to an image using ImageSharp.
    /// </summary>
    [HttpGet("{id}/render-image")]
    public async Task<IActionResult> RenderDashboardImage(
        string id,
        [FromQuery] string format = "jpeg",
        [FromQuery] bool refresh = false)
    {
        if (!DashboardId.TryParse(id, out var dashboardId))
        {
            return BadRequest("Invalid dashboard ID");
        }

        var dashboard = dashboardService.GetDashboardById(dashboardId);
        if (dashboard.HasNoValue)
            return NotFound("Dashboard not found");

        if (dashboard.Value.UserId != CurrentUserId)
            return Forbid();

        if (dashboard.Value.LayoutConfig == null)
            return BadRequest("Dashboard has no layout configuration. Open the designer and create a layout first.");

        try
        {
            var layoutToRender = dashboard.Value.GetMergedLayoutConfig();

            using var rawImage = await dashboardImageRenderingService.RenderDashboardImageAsync(
                dashboard.Value.Id.ToString(),
                layoutToRender,
                HttpContext.RequestAborted,
                bypassCache: refresh);

            using IImage image = ImageAdapter<SixLabors.ImageSharp.PixelFormats.Rgba32>.Wrap(rawImage);

            var (contentType, encoder) = GetEncoder(format);
            return await ConvertToResult(image, encoder, contentType);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to render dashboard image: {ex.Message}");
        }
    }

    /// <summary>
    /// Renders a transient layout without persisting it. The designer uses this endpoint so the
    /// rendered preview includes the user's current unsaved changes.
    /// </summary>
    [HttpPost("{id}/render-image")]
    public async Task<IActionResult> RenderTransientDashboardImage(
        string id,
        [FromBody] EPaperDashboard.Models.LayoutConfig layout,
        [FromQuery] string format = "png",
        [FromQuery] bool refresh = true)
    {
        if (!DashboardId.TryParse(id, out var dashboardId))
            return BadRequest("Invalid dashboard ID");

        var dashboard = dashboardService.GetDashboardById(dashboardId);
        if (dashboard.HasNoValue)
            return NotFound("Dashboard not found");

        if (dashboard.Value.UserId != CurrentUserId)
            return Forbid();

        var validationError = TransientLayoutValidator.Validate(layout);
        if (validationError is not null)
            return BadRequest(validationError);

        try
        {
            var layoutToRender = dashboard.Value.GetMergedLayoutConfig(layout);
            using var rawImage = await dashboardImageRenderingService.RenderDashboardImageAsync(
                dashboard.Value.Id.ToString(),
                layoutToRender,
                HttpContext.RequestAborted,
                bypassCache: refresh,
                cacheResult: false);
            using IImage image = ImageAdapter<SixLabors.ImageSharp.PixelFormats.Rgba32>.Wrap(rawImage);

            var (contentType, encoder) = GetEncoder(format);
            return await ConvertToResult(image, encoder, contentType);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to render dashboard image: {ex.Message}");
        }
    }

    /// <summary>
    /// Produces the canonical image used beneath the dashboard designer interaction layer.
    /// The response also contains renderer-resolved widget geometry and echoes the client
    /// revision so out-of-order renders can be discarded safely.
    /// </summary>
    [HttpPost("{id}/designer-preview")]
    public async Task<IActionResult> RenderDesignerPreview(
        string id,
        [FromBody] DashboardDesignerPreviewRequest request)
    {
        if (!DashboardId.TryParse(id, out var dashboardId))
            return BadRequest("Invalid dashboard ID");

        var dashboard = dashboardService.GetDashboardById(dashboardId);
        if (dashboard.HasNoValue)
            return NotFound("Dashboard not found");

        if (dashboard.Value.UserId != CurrentUserId)
            return Forbid();

        if (request.Layout is null)
            return BadRequest("A layout is required.");

        var validationError = TransientLayoutValidator.Validate(request.Layout);
        if (validationError is not null)
            return BadRequest(validationError);

        try
        {
            // The designer posts its complete transient state, including generated widgets. Do
            // not merge the persisted layout here or unsaved enable/disable changes could be
            // ignored and generated widgets could be duplicated.
            var layoutToRender = request.Layout;
            var render = await dashboardImageRenderingService.RenderDesignerPreviewAsync(
                dashboard.Value.Id.ToString(),
                layoutToRender,
                HttpContext.RequestAborted,
                bypassCache: request.RefreshData);
            using var image = render.Image;

            await using var stream = new MemoryStream();
            // Previews are short-lived and re-rendered on every edit; favour encode speed over size.
            await image.SaveAsync(
                stream,
                new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed },
                HttpContext.RequestAborted);

            var png = stream.ToArray();
            var token = designerPreviewImageStore.Add(
                CurrentUserId.ToString(), dashboard.Value.Id.ToString(), png);
            return Ok(new DashboardDesignerPreviewResponse(
                request.Revision,
                image.Width,
                image.Height,
                $"/api/dashboards/{id}/designer-preview/{token}/image",
                timeProvider.GetUtcNow(),
                dashboardImageRenderingService.ResolveWidgetGeometry(layoutToRender),
                render.SourceStatuses));
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to render dashboard preview: {ex.Message}");
        }
    }

    [HttpGet("{id}/designer-preview/{token}/image")]
    public IActionResult GetDesignerPreviewImage(string id, string token)
    {
        if (!DashboardId.TryParse(id, out var dashboardId))
            return BadRequest("Invalid dashboard ID");

        var dashboard = dashboardService.GetDashboardById(dashboardId);
        if (dashboard.HasNoValue) return NotFound();
        if (dashboard.Value.UserId != CurrentUserId) return Forbid();

        return designerPreviewImageStore.TryGet(
            token, CurrentUserId.ToString(), dashboard.Value.Id.ToString(), out var png)
                ? File(png, "image/png")
                : NotFound();
    }

    /// <summary>
    /// Resolves all data needed by a transient designer layout through the production data
    /// collector. The designer consumes this snapshot instead of calling each source separately.
    /// </summary>
    [HttpPost("{id}/preview-data")]
    public async Task<IActionResult> GetTransientPreviewData(
        string id,
        [FromBody] EPaperDashboard.Models.LayoutConfig layout)
    {
        if (!DashboardId.TryParse(id, out var dashboardId))
            return BadRequest("Invalid dashboard ID");

        var dashboard = dashboardService.GetDashboardById(dashboardId);
        if (dashboard.HasNoValue)
            return NotFound("Dashboard not found");

        if (dashboard.Value.UserId != CurrentUserId)
            return Forbid();

        var validationError = TransientLayoutValidator.Validate(layout);
        if (validationError is not null)
            return BadRequest(validationError);

        var layoutToResolve = dashboard.Value.GetMergedLayoutConfig(layout);
        var data = await dashboardImageRenderingService.FetchDashboardDataAsync(
            dashboard.Value.Id.ToString(),
            layoutToResolve,
            HttpContext.RequestAborted,
            bypassCache: true);

        return Ok(DashboardPreviewData.FromSsrData(data, timeProvider));
    }

    /// <summary>
    /// Renders a preview of the dashboard. Supports both Custom (ImageSharp) and
    /// HomeAssistant (Playwright) rendering modes. Protected by cookie auth.
    /// </summary>
    [HttpGet("{id}/preview")]
    public async Task<IActionResult> PreviewDashboard(string id, [FromQuery] string format = "png")
    {
        if (!DashboardId.TryParse(id, out var dashboardId))
            return BadRequest("Invalid dashboard ID");

        var dashboard = dashboardService.GetDashboardById(dashboardId);
        if (dashboard.HasNoValue)
            return NotFound("Dashboard not found");

        if (dashboard.Value.UserId != CurrentUserId)
            return Forbid();

        var (width, height) = dashboard.Value.GetEffectiveSize();
        var imageSize = new Size(width, height);

        return dashboard.Value.RenderingMode == RenderingMode.Custom
            ? await RenderCustomPreview(dashboard.Value, imageSize, format)
            : await RenderHomeAssistantPreview(dashboard.Value, imageSize, format);
    }

    private async Task<IActionResult> RenderCustomPreview(Dashboard dashboard, Size imageSize, string format)
    {
        if (dashboard.LayoutConfig == null)
            return BadRequest("Dashboard has no layout configuration. Open the designer and create a layout first.");

        try
        {
            var layoutToRender = dashboard.GetMergedLayoutConfig();

            using var rawImage = await dashboardImageRenderingService.RenderDashboardImageAsync(
                dashboard.Id.ToString(),
                layoutToRender,
                HttpContext.RequestAborted);

            using IImage image = ImageAdapter<SixLabors.ImageSharp.PixelFormats.Rgba32>.Wrap(rawImage);

            var (contentType, encoder) = GetEncoder(format);
            return await ConvertToResult(image, encoder, contentType);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to render dashboard image: {ex.Message}");
        }
    }

    private async Task<IActionResult> RenderHomeAssistantPreview(Dashboard dashboard, Size imageSize, string format)
    {
        var target = homeAssistantTargets.Resolve(dashboard);
        if (target.IsFailure) return NotFound(target.Error);

        var (contentType, encoder) = GetEncoder(format);
        var result = await renderingService
            .RenderDashboardAsync(CurrentUserId, dashboard.Id, target.Value.DashboardUri, imageSize, target.Value.Tokens, HttpContext.RequestAborted);

        return await result.Match(
            image => ConvertToResult(image, encoder, contentType),
            error => Task.FromResult<IActionResult>(BadRequest(error)));
    }

    private async Task<IActionResult> ConvertToResult(IImage image, IImageEncoder encoder, string contentType)
    {
        var outStream = new MemoryStream();
        await image.SaveAsync(outStream, encoder);
        outStream.Seek(0, SeekOrigin.Begin);
        return File(outStream, contentType);
    }

    private static (string contentType, IImageEncoder encoder) GetEncoder(string format) => format switch
    {
        "jpeg" => ("image/jpeg", new JpegEncoder()),
        "bmp" => ("image/bmp", new BmpEncoder()),
        "png" => ("image/png", new PngEncoder()),
        _ => throw new NotSupportedException($"Format is not supported: {format}")
    };
}
