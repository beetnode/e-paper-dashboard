using Microsoft.AspNetCore.Mvc;
using EPaperDashboard.Services.Rendering;
using SixLabors.ImageSharp.Processing;
using EPaperDashboard.Utilities;
using CSharpFunctionalExtensions;
using EPaperDashboard.Models.Rendering;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing.Processors.Dithering;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Guards;
using EPaperDashboard.Services.Plugins.HomeAssistant;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/render")]
[Authorize(Policy = "ApiKeyPolicy")]
[DeviceAccessible]
public sealed class RenderToImageController(
	IPageToImageRenderingService renderingService,
	DashboardService dashboardService,
	DeviceService deviceService,
	DashboardImageRenderingService dashboardImageRenderingService,
	HomeAssistantDashboardTargetResolver homeAssistantTargets) : ControllerBase
{
	[HttpGet("binary")]
	public async Task<IActionResult> GetAsBinary(
		[FromHeader(Name = HttpHeaderNames.ApiKeyHeaderName)] string apiKey,
		[FromQuery] bool shouldDither = false) =>
		await RenderImage(apiKey, "bin", (dashboard, image) =>
		{
			var result = image
				.Quantize(Palettes.RedBlackWhite, GetDither(shouldDither))
				.RotateFlip(RotateMode.Rotate90, FlipMode.Horizontal);
			return dashboard.Orientation == DashboardOrientation.Portrait
				? result.Rotate(RotateMode.Rotate90)
				: result;
		});

	[HttpGet("converted")]
	public async Task<IActionResult> GetAsConvertedsImage(
		[FromHeader(Name = HttpHeaderNames.ApiKeyHeaderName)] string apiKey,
		[FromQuery] string format = "jpeg",
		[FromQuery] bool shouldDither = false) =>
		await RenderImage(apiKey, format, (_, image) =>
			image.Quantize(Palettes.RedBlackWhite, GetDither(shouldDither)));

	[HttpGet("original")]
	public async Task<IActionResult> GetAsImage(
		[FromHeader(Name = HttpHeaderNames.ApiKeyHeaderName)] string apiKey,
		[FromQuery] string format = "jpeg") =>
		await RenderImage(apiKey, format);

	[HttpGet("health")]
    public async Task<IActionResult> GetHealth([FromHeader(Name = HttpHeaderNames.ApiKeyHeaderName)] string apiKey)
    {
        var dashboard = ResolveDashboardByApiKey(apiKey);
        if (dashboard.HasNoValue) return NotFound();
        var uri = homeAssistantTargets.ResolveUri(dashboard.Value);
        if (uri.IsFailure) return NotFound();
        return Ok(await renderingService.GetHealth(dashboard.Value.UserId, dashboard.Value.Id, uri.Value));
    }

	private Maybe<Dashboard> ResolveDashboardByApiKey(string apiKey)
	{
		var device = deviceService.GetDeviceByApiKey(apiKey);
		if (device.HasValue && device.Value.DashboardId != DashboardId.Empty)
		{
			device.Value.LastSeenAt = DateTimeOffset.UtcNow;
			deviceService.UpdateDevice(device.Value);
			return dashboardService.GetDashboardById(device.Value.DashboardId)
                .Where(d => d.UserId == device.Value.UserId);
		}

		return Maybe.None;
	}

	private async Task<IActionResult> RenderImage(
		string apiKey,
		string format,
		Func<Dashboard, IImage, IImage>? transform = null)
	{
		var dashboardResult = ResolveDashboardByApiKey(apiKey);
		if (dashboardResult.HasNoValue)
		{
			return NotFound("Dashboard not found for this API key");
		}

		var dashboard = dashboardResult.Value;
		var (width, height) = dashboard.GetEffectiveSize();
		var imageSize = new Size(width, height);

		if (dashboard.RenderingMode == RenderingMode.Custom)
		{
			return await RenderCustomLayoutImage(dashboard, imageSize, format, transform);
		}
		else
		{
			return await RenderHomeAssistantImage(dashboard, imageSize, format, transform);
		}
	}

	private async Task<IActionResult> RenderCustomLayoutImage(
		Dashboard dashboard,
		Size imageSize,
		string format,
		Func<Dashboard, IImage, IImage>? transform = null)
	{
		if (dashboard.LayoutConfig == null)
		{
			return BadRequest("Dashboard has no layout configuration. Open the designer and create a layout first.");
		}

		var (contentType, encoder) = GetEncoder(format);

		try
		{
			var layoutToRender = dashboard.GetMergedLayoutConfig();

			using var rawImage = await dashboardImageRenderingService.RenderDashboardImageAsync(
				dashboard.Id.ToString(),
				layoutToRender,
				HttpContext.RequestAborted);

			using IImage image = ImageAdapter<SixLabors.ImageSharp.PixelFormats.Rgba32>.Wrap(rawImage);
			var resultImage = transform?.Invoke(dashboard, image) ?? image;

			dashboard.LastUpdateTime = DateTimeOffset.UtcNow;
			dashboardService.UpdateDashboard(dashboard);

			return await ConvertToResult(resultImage, encoder, contentType);
		}
		catch (Exception ex)
		{
			return StatusCode(500, $"Failed to render dashboard image: {ex.Message}");
		}
	}

	private async Task<IActionResult> RenderHomeAssistantImage(
		Dashboard dashboard,
		Size imageSize,
		string format,
		Func<Dashboard, IImage, IImage>? transform = null)
	{
		var target = homeAssistantTargets.Resolve(dashboard);
		if (target.IsFailure) return NotFound(target.Error);

		var (contentType, encoder) = GetEncoder(format);
		var result = await renderingService
			.RenderDashboardAsync(dashboard.UserId, dashboard.Id, target.Value.DashboardUri, imageSize, target.Value.Tokens, HttpContext.RequestAborted)
			.Map(image => transform?.Invoke(dashboard, image) ?? image);

		if (result.IsSuccess)
		{
			dashboard.LastUpdateTime = DateTimeOffset.UtcNow;
			dashboardService.UpdateDashboard(dashboard);
		}

		return await result.Match(
			image => ConvertToResult(image, encoder, contentType),
			error => Task.FromResult<IActionResult>(BadRequest(error)));
	}

	private static IDither? GetDither(bool shouldDither) =>
		shouldDither ? KnownDitherings.JarvisJudiceNinke : null;

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
		"bin" => ("application/octet-stream", new BlackRedWhiteBinaryEncoder()),
		_ => throw new NotSupportedException($"Format is not supported: {format}")
	};
}
