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
	IDeploymentStrategy deploymentStrategy,
	IEnvironmentConfiguration environmentConfiguration) : ControllerBase
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
        var uri = GetDashboardUri(dashboard.Value, deploymentStrategy);
        if (uri.HasNoValue) return NotFound();
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
		var dashboardInfo = GetDashboardInfo(dashboard, deploymentStrategy, environmentConfiguration);
		if (dashboardInfo.HasNoValue)
		{
			var hint = deploymentStrategy.IsAutoConnected
				? "Dashboard configuration incomplete. The 'Home Assistant Dashboard' rendering mode requires " +
				  "a Long-Lived Access Token. Create one in Home Assistant (Profile → Long-Lived Access Tokens) " +
				  "and set it on the dashboard."
				: "Dashboard configuration incomplete. Ensure Host, Path, and Access Token are set.";
			return NotFound(hint);
		}

		var (contentType, encoder) = GetEncoder(format);
		var result = await renderingService
			.RenderDashboardAsync(dashboard.UserId, dashboard.Id, dashboardInfo.Value.DashboardUri, imageSize, dashboardInfo.Value.Tokens, HttpContext.RequestAborted)
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

	private static Maybe<(Uri DashboardUri, HassTokens Tokens)> GetDashboardInfo(
		Dashboard dashboard, IDeploymentStrategy deploymentStrategy, IEnvironmentConfiguration environmentConfiguration)
	{
		var (strategyHost, _) = deploymentStrategy.GetHomeAssistantConnection(dashboard);

		var host = dashboard.Host;
		if (string.IsNullOrWhiteSpace(host) && deploymentStrategy.Mode != DeploymentMode.Standalone)
		{
			// For browser-based rendering (Playwright), we need the actual HA web UI URL.
			// The supervisor API proxy doesn't serve the HA frontend, so use the
			// direct internal HA URL in addon mode.
			host = deploymentStrategy.Mode == DeploymentMode.Addon
				? Constants.HomeAssistantInternalUrl
				: strategyHost;
		}

		// For Playwright rendering, always use the dashboard's stored access token.
		// In addon mode, this is an auto-created long-lived HA Core token (not the supervisor token,
		// which only works via the supervisor proxy and can't authenticate with HA frontend directly).
		var accessToken = dashboard.AccessToken;

		if (string.IsNullOrWhiteSpace(accessToken)
			|| !Uri.TryCreate(host, UriKind.Absolute, out var hostUri)
			|| !Uri.TryCreate(dashboard.Path, UriKind.Relative, out var pathUri))
		{
			return Maybe.None;
		}

		var hassUrl = hostUri.AbsoluteUri.TrimEnd('/');

		// For OAuth-generated long-lived tokens, ClientId is not used for auth
		// Use ClientUri if configured, otherwise use the HA host URL as a placeholder
		var clientId = environmentConfiguration.ClientUri?.AbsoluteUri.TrimEnd('/') ?? hassUrl;

		return (new Uri(hostUri, pathUri), new HassTokens(accessToken, "Bearer", hassUrl, clientId));
	}

	private static Maybe<Uri> GetDashboardUri(Dashboard dashboard, IDeploymentStrategy deploymentStrategy)
	{
		var host = dashboard.Host;
		if (string.IsNullOrWhiteSpace(host) && deploymentStrategy.Mode != DeploymentMode.Standalone)
		{
			// For browser-based rendering, use HA direct URL in addon mode
			host = deploymentStrategy.Mode == DeploymentMode.Addon
				? Constants.HomeAssistantInternalUrl
				: deploymentStrategy.GetHomeAssistantConnection(dashboard).host;
		}

		return Uri.TryCreate(host, UriKind.Absolute, out var hostUri) &&
			Uri.TryCreate(dashboard.Path, UriKind.Relative, out var pathUri)
			? new Uri(hostUri, pathUri)
			: Maybe.None;
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
