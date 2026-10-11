using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.Services.Providers;
using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using RectangleF = SixLabors.ImageSharp.RectangleF;

namespace EPaperDashboard.Services.Rendering;

/// <summary>
/// Renders a custom dashboard layout directly to an ImageSharp image,
/// without generating HTML or using Playwright. Coordinates widget renderers
/// and provides the overall rendering pipeline.
/// </summary>
public sealed class DashboardImageRenderingService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly ISsrDataProvider _ssrDataProvider;
    private readonly ILogger<DashboardImageRenderingService> _logger;
    private readonly RenderingUtilities _utils;
    private readonly Dictionary<string, IWidgetRenderer> _renderers;
    private readonly IMemoryCache _cache;
    private readonly Plugins.PluginDataRouter? _providerRouter;

    public DashboardImageRenderingService(
        ISsrDataProvider ssrDataProvider,
        ILogger<DashboardImageRenderingService> logger,
        RenderingUtilities utils,
        IEnumerable<IWidgetRenderer> widgetRenderers,
        IMemoryCache cache,
        Plugins.ProviderRegistry? plugins = null,
        Plugins.PluginDataRouter? providerRouter = null)
    {
        _ssrDataProvider = ssrDataProvider;
        _logger = logger;
        _utils = utils;
        _renderers = widgetRenderers.ToDictionary(r => r.WidgetType, r => r);
        if (plugins is not null)
            foreach (var widget in plugins.Widgets)
            {
                if (_renderers.ContainsKey(widget.Type)) continue;
                if (widget.RenderAs is null || !_renderers.TryGetValue(widget.RenderAs, out var renderer))
                    throw new InvalidOperationException($"Plugin widget {widget.Type} requires renderer {widget.RenderAs}");
                _renderers.Add(widget.Type, renderer);
            }
        _cache = cache;
        _providerRouter = providerRouter;
    }

    /// <summary>
    /// Returns the shared <see cref="RenderingUtilities"/> instance for DI consumers
    /// that need to create widget renderers with font/icon support.
    /// </summary>
    public RenderingUtilities Utils => _utils;

    public Task<SsrData> FetchDashboardDataAsync(
        string dashboardId,
        Models.LayoutConfig layoutConfig,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
    {
        var layout = ConvertLayout(layoutConfig);
        return _ssrDataProvider.FetchSsrDataAsync(dashboardId, layout, cancellationToken, bypassCache);
    }

    /// <summary>
    /// Renders a transient designer image and returns the source statuses collected for that
    /// exact render. This keeps designer diagnostics consistent with the pixels shown to users.
    /// </summary>
    public async Task<(Image<Rgba32> Image, IReadOnlyDictionary<string, DataSourceStatus> SourceStatuses)>
        RenderDesignerPreviewAsync(
            string dashboardId,
            Models.LayoutConfig layoutConfig,
            CancellationToken cancellationToken = default,
            bool bypassCache = false)
    {
        var layout = ConvertLayout(layoutConfig);
        var data = await _ssrDataProvider.FetchSsrDataAsync(
            dashboardId, layout, cancellationToken, bypassCache);
        var image = await RenderToImageAsync(layout, data, cancellationToken);
        return (image, data.SourceStatuses);
    }

    /// <summary>
    /// Resolves the same widget and content rectangles used by the image renderer. The designer
    /// uses these values for interaction overlays without duplicating rendering geometry rules.
    /// </summary>
    public IReadOnlyList<WidgetRenderGeometry> ResolveWidgetGeometry(Models.LayoutConfig layoutConfig)
    {
        var layout = ConvertLayout(layoutConfig);
        var inset = layout.WidgetBorder + layout.WidgetPadding;

        return layout.Widgets.Select(widget =>
        {
            var (x, y, width, height) = RenderingUtilities.ResolvePixelPosition(widget.Position, layout);
            var contentWidth = Math.Max(0, width - inset * 2);
            var contentHeight = Math.Max(0, height - inset * 2);
            var contentBounds = new RenderRectangle(
                (int)(x + inset), (int)(y + inset), contentWidth, contentHeight);
            var editableRenderer = _renderers.TryGetValue(widget.Type, out var renderer)
                ? renderer as IEditableWidgetRenderer
                : null;
            IReadOnlyList<EditableWidgetElementGeometry> elements = [];
            var editable = editableRenderer is not null;
            if (editableRenderer is not null)
            {
                try
                {
                    elements = editableRenderer.BuildRenderPlan(widget, new RectangleF(
                        (float)contentBounds.X,
                        (float)contentBounds.Y,
                        (float)contentBounds.Width,
                        (float)contentBounds.Height)).Elements;
                }
                catch (Exception ex)
                {
                    editable = false;
                    _logger.LogWarning(ex,
                        "Failed to resolve editable geometry for widget {WidgetId} ({WidgetType})",
                        widget.Id, widget.Type);
                }
            }

            return new WidgetRenderGeometry(
                widget.Id,
                widget.Type,
                new RenderRectangle(x, y, width, height),
                contentBounds,
                editable,
                elements);
        }).ToList();
    }

    /// <summary>
    /// Renders the dashboard to an ImageSharp image using the typed layout model and live HA data.
    /// Returns a cached result if the same dashboard was rendered within the last 30 seconds,
    /// unless <paramref name="bypassCache"/> requests a fresh data snapshot. Transient callers
    /// should set <paramref name="cacheResult"/> to false so unsaved layouts do not retain raw
    /// pixel buffers in the application cache.
    /// </summary>
    public async Task<Image<Rgba32>> RenderDashboardImageAsync(
        string dashboardId,
        Models.LayoutConfig layoutConfig,
        CancellationToken cancellationToken = default,
        bool bypassCache = false,
        bool cacheResult = true)
    {
        var layoutHash = ComputeLayoutHash(layoutConfig);
        var cacheKey = $"ssr:{dashboardId}:{_providerRouter?.CacheVersion(dashboardId)}:{layoutHash}";

        if (!bypassCache && _cache.TryGetValue<CachedRender>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogDebug("SSR: Returning cached render for dashboard {DashboardId}", dashboardId);
            var img = new Image<Rgba32>(cached.Width, cached.Height);
            if (img.DangerousTryGetSinglePixelMemory(out var memory))
            {
                cached.PixelData.AsSpan().CopyTo(memory.Span);
            }
            return img;
        }

        var layout = ConvertLayout(layoutConfig);
        var data = await _ssrDataProvider.FetchSsrDataAsync(dashboardId, layout, cancellationToken, bypassCache);
        var image = await RenderToImageAsync(layout, data, cancellationToken);

        if (cacheResult)
        {
            // Cache raw pixel data — avoids PNG encode on write + PNG decode on read.
            // Transient designer renders deliberately skip this potentially large allocation.
            var pixelData = new Rgba32[image.Width * image.Height];
            image.CopyPixelDataTo(pixelData);
            _cache.Set(cacheKey, new CachedRender(image.Width, image.Height, pixelData), CacheDuration);
        }

        return image;
    }

    private sealed record CachedRender(int Width, int Height, Rgba32[] PixelData);

    // =============================================
    // LAYOUT CONVERSION (typed model → rendering record)
    // =============================================

    private static LayoutConfig ConvertLayout(Models.LayoutConfig src)
    {
        var cs = src.ColorScheme;
        var colorScheme = new ColorSchemeConfig(
            Name: cs.Name ?? "Default",
            Variant: cs.Variant,
            Palette: cs.Palette?.ToArray() ?? ["#000000", "#ffffff", "#ff0000"],
            Background: DefaultIfEmpty(cs.Background, "#ffffff"),
            CanvasBackgroundColor: DefaultIfEmpty(cs.CanvasBackgroundColor, "#ffffff"),
            WidgetBackgroundColor: DefaultIfEmpty(cs.WidgetBackgroundColor, "#ffffff"),
            WidgetBorderColor: DefaultIfEmpty(cs.WidgetBorderColor, "#000000"),
            WidgetTitleTextColor: DefaultIfEmpty(cs.WidgetTitleTextColor, "#000000"),
            WidgetTextColor: DefaultIfEmpty(cs.WidgetTextColor, "#000000"),
            IconColor: DefaultIfEmpty(cs.IconColor, "#ff0000"),
            Foreground: DefaultIfEmpty(cs.Foreground, "#000000"),
            Accent: DefaultIfEmpty(cs.Accent, "#ff0000"),
            Text: DefaultIfEmpty(cs.Text, "#000000")
        );

        var widgets = src.Widgets
            .Where(w => !string.IsNullOrEmpty(w.Id) && !string.IsNullOrEmpty(w.Type))
            .Select(w => new WidgetConfigEntry(
                Id: w.Id,
                Type: w.Type,
                Position: new WidgetPositionConfig(
                    w.Position.X, w.Position.Y, w.Position.W, w.Position.H,
                    w.Position.PixelX, w.Position.PixelY,
                    w.Position.PixelWidth, w.Position.PixelHeight),
                Config: w.Config.ValueKind != JsonValueKind.Undefined ? w.Config.Clone() : default,
                ColorOverrides: w.ColorOverrides is { } co
                    ? new WidgetColorOverridesConfig(
                        co.WidgetBackgroundColor, co.WidgetBorderColor,
                        co.WidgetTitleTextColor, co.WidgetTextColor, co.IconColor)
                    : null,
                TitleOverride: w.TitleOverride,
                ShowTitle: w.ShowTitle,
                FontOverrides: w.FontOverrides is { } fo
                    ? new WidgetFontOverridesConfig(fo.TitleFontSize, fo.TextFontSize)
                    : null))
            .ToList();

        return new LayoutConfig(
            Width: src.Width,
            Height: src.Height,
            GridCols: src.GridCols > 0 ? src.GridCols : 12,
            GridRows: src.GridRows > 0 ? src.GridRows : 8,
            ColorScheme: colorScheme,
            Widgets: widgets,
            CanvasPadding: src.CanvasPadding,
            WidgetGap: src.WidgetGap,
            WidgetBorder: src.WidgetBorder,
            WidgetPadding: src.WidgetPadding,
            TitleFontSize: src.TitleFontSize > 0 ? src.TitleFontSize : 16,
            TextFontSize: src.TextFontSize > 0 ? src.TextFontSize : 14,
            TitleFontWeight: src.TitleFontWeight > 0 ? src.TitleFontWeight : 700,
            TextFontWeight: src.TextFontWeight > 0 ? src.TextFontWeight : 400
        );
    }

    private static string DefaultIfEmpty(string? value, string fallback)
        => string.IsNullOrEmpty(value) ? fallback : value;

    // =============================================
    // IMAGE RENDERING
    // =============================================

    private async Task<Image<Rgba32>> RenderToImageAsync(LayoutConfig layout, SsrData data, CancellationToken cancellationToken)
    {
        // Clamp dimensions to a safe range to prevent excessive memory allocation
        var width = Math.Clamp(layout.Width, 1, 4096);
        var height = Math.Clamp(layout.Height, 1, 4096);
        var image = new Image<Rgba32>(width, height);

        var canvasBg = ColorUtils.ParseColor(layout.ColorScheme.CanvasBackgroundColor);
        image.Mutate(ctx => ctx.Fill(canvasBg));

        foreach (var widget in layout.Widgets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RenderWidgetAsync(image, widget, layout, data, cancellationToken);
        }

        return image;
    }

    private async Task RenderWidgetAsync(Image<Rgba32> image, WidgetConfigEntry widget, LayoutConfig layout, SsrData data, CancellationToken cancellationToken)
    {
        var (px, py, pw, ph) = RenderingUtilities.ResolvePixelPosition(widget.Position, layout);
        var widgetRect = new RectangleF((float)px, (float)py, (float)pw, (float)ph);

        DrawWidgetContainer(image, widget, layout, widgetRect);

        var border = layout.WidgetBorder;
        var padding = layout.WidgetPadding;
        var inset = border + padding;
        var contentRect = new RectangleF(
            widgetRect.X + inset,
            widgetRect.Y + inset,
            Math.Max(0, widgetRect.Width - inset * 2),
            Math.Max(0, widgetRect.Height - inset * 2));

        if (contentRect.Width <= 0 || contentRect.Height <= 0)
            return;

        try
        {
            if (_renderers.TryGetValue(widget.Type, out var renderer))
            {
                // Render into a clipped sub-image to prevent widget content overflow
                var clipW = (int)Math.Ceiling(contentRect.Width);
                var clipH = (int)Math.Ceiling(contentRect.Height);
                using var tempImage = new Image<Rgba32>(clipW, clipH);
                var localRect = new RectangleF(0, 0, contentRect.Width, contentRect.Height);
                await renderer.RenderAsync(tempImage, widget, layout, data, localRect, cancellationToken);
                image.Mutate(ctx => ctx.DrawImage(tempImage,
                    new SixLabors.ImageSharp.Point((int)contentRect.X, (int)contentRect.Y), 1f));
            }
            else
            {
                RenderPlaceholder(image, widget, layout, contentRect, widget.Type);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render widget {WidgetId} of type {WidgetType}", widget.Id, widget.Type);
            RenderErrorIndicator(image, widget, layout, contentRect);
        }
    }

    // =============================================
    // WIDGET CONTAINER
    // =============================================

    private static void DrawWidgetContainer(Image<Rgba32> image, WidgetConfigEntry widget, LayoutConfig layout, RectangleF rect)
    {
        var cs = layout.ColorScheme;
        var bg = ColorUtils.ParseColor(widget.ColorOverrides?.WidgetBackgroundColor ?? cs.WidgetBackgroundColor);
        var bc = ColorUtils.ParseColor(widget.ColorOverrides?.WidgetBorderColor ?? cs.WidgetBorderColor);
        var borderWidth = layout.WidgetBorder;

        image.Mutate(ctx =>
        {
            if (borderWidth > 0)
            {
                ctx.Fill(bc, new RectangularPolygon(rect));
                ctx.Fill(bg, new RectangularPolygon(
                    rect.X + borderWidth,
                    rect.Y + borderWidth,
                    Math.Max(0, rect.Width - borderWidth * 2),
                    Math.Max(0, rect.Height - borderWidth * 2)));
            }
            else
            {
                ctx.Fill(bg, new RectangularPolygon(rect));
            }
        });
    }

    // =============================================
    // PLACEHOLDER (for unsupported widget types)
    // =============================================

    private void RenderPlaceholder(Image<Rgba32> image, WidgetConfigEntry widget, LayoutConfig layout, RectangleF contentRect, string label)
    {
        var ctx = WidgetRenderContext.Create(widget, layout);
        TextDrawing.DrawCenteredText(image, label, _utils.GetFont(ctx.TextFontSize), ctx.TextColor, contentRect);
    }

    private void RenderErrorIndicator(Image<Rgba32> image, WidgetConfigEntry widget, LayoutConfig layout, RectangleF contentRect)
    {
        var ctx = WidgetRenderContext.Create(widget, layout);
        var errorColor = ColorUtils.WithOpacity(ctx.TextColor, 0.4f);
        var font = _utils.GetFont(Math.Max(10, ctx.TextFontSize - 2));
        TextDrawing.DrawCenteredText(image, $"⚠ {widget.Type}", font, errorColor, contentRect);
    }

    private static string ComputeLayoutHash(Models.LayoutConfig layoutConfig)
    {
        var json = JsonSerializer.Serialize(layoutConfig);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hashBytes)[..16];
    }
}
