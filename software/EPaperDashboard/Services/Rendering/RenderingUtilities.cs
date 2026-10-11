using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.Utilities;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;
using RectangleF = SixLabors.ImageSharp.RectangleF;

namespace EPaperDashboard.Services.Rendering;

/// <summary>
/// Shared drawing utilities used by widget renderers and the main rendering service.
/// Font management, FA icon drawing, inline-icon text, JSON helpers, and layout utilities.
/// Color helpers live in <see cref="ColorUtils"/>, text drawing in <see cref="TextDrawing"/>,
/// app-icon drawing in <see cref="IconDrawing"/>, and markdown helpers in <see cref="MarkdownHelpers"/>.
/// </summary>
public sealed class RenderingUtilities
{
    private readonly FontFamily _fontFamily;
    private readonly FontAwesomeIconRegistry _iconRegistry;
    private readonly ConcurrentDictionary<(int Size, FontStyle Style), Font> _fontCache = new();

    public RenderingUtilities(FontFamily fontFamily, FontAwesomeIconRegistry iconRegistry)
    {
        _fontFamily = fontFamily;
        _iconRegistry = iconRegistry;
    }

    public static RenderingUtilities Create(IWebHostEnvironment env, FontAwesomeIconRegistry iconRegistry)
    {
        var fontFamily = LoadFontFamily(env.WebRootPath);
        return new RenderingUtilities(fontFamily, iconRegistry);
    }

    private static FontFamily LoadFontFamily(string? webRootPath)
    {
        if (!string.IsNullOrEmpty(webRootPath))
        {
            var fontsDir = System.IO.Path.Combine(webRootPath, "fonts");
            if (Directory.Exists(fontsDir))
            {
                var collection = new FontCollection();
                foreach (var file in Directory.GetFiles(fontsDir, "*.ttf")
                    .Concat(Directory.GetFiles(fontsDir, "*.otf")))
                {
                    try { return collection.Add(file); }
                    catch { /* skip unreadable font files */ }
                }
            }
        }

        if (SystemFonts.TryGet("Inter", out var systemFamily)) return systemFamily;
        if (SystemFonts.TryGet("Roboto", out systemFamily)) return systemFamily;
        if (SystemFonts.TryGet("DejaVu Sans", out systemFamily)) return systemFamily;
        if (SystemFonts.TryGet("Liberation Sans", out systemFamily)) return systemFamily;
        if (SystemFonts.TryGet("Arial", out systemFamily)) return systemFamily;
        if (SystemFonts.TryGet("Helvetica", out systemFamily)) return systemFamily;
        if (SystemFonts.TryGet("Segoe UI", out systemFamily)) return systemFamily;

        foreach (var family in SystemFonts.Families)
            return family;

        throw new InvalidOperationException("No fonts available on the system for rendering.");
    }

    // =============================================
    // FONT HELPERS
    // =============================================

    public Font GetFont(int size, FontStyle style = FontStyle.Regular)
    {
        return _fontCache.GetOrAdd((size, style), k => _fontFamily.CreateFont(k.Size, k.Style));
    }

    public Font GetFont(int size, int weight)
    {
        var style = weight >= 700 ? FontStyle.Bold : FontStyle.Regular;
        return GetFont(size, style);
    }

    // =============================================
    // FA ICON DRAWING
    // =============================================

    public void DrawFaIcon(Image<Rgba32> image, string? iconClass, Color color, RectangleF bounds)
    {
        if (string.IsNullOrEmpty(iconClass) || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (!_iconRegistry.TryGetIcon(iconClass, out var entry))
            return;

        var path = _iconRegistry.GetParsedPath(iconClass, entry);
        if (path is null)
            return;

        var scale = Math.Min(bounds.Width / entry.VbW, bounds.Height / entry.VbH);
        var offsetX = bounds.X + (bounds.Width - entry.VbW * scale) / 2f;
        var offsetY = bounds.Y + (bounds.Height - entry.VbH * scale) / 2f;

        var matrix = System.Numerics.Matrix3x2.CreateScale(scale) *
                     System.Numerics.Matrix3x2.CreateTranslation(offsetX, offsetY);

        var transformed = path.Transform(matrix);
        image.Mutate(ctx => ctx.Fill(color, transformed));
    }

    // =============================================
    // JSON PROPERTY HELPERS
    // =============================================

    public static string? GetStringProp(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    public static int? GetIntProp(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    public static double? GetDoubleProp(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    public static bool? GetBoolProp(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var p)
            ? p.ValueKind == JsonValueKind.True ? true : p.ValueKind == JsonValueKind.False ? false : null
            : null;

    public static string[]? GetStringArrayProp(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var p) || p.ValueKind != JsonValueKind.Array)
            return null;
        return p.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!)
            .ToArray();
    }

    public static double? GetBadgeDoubleProp(JsonElement badge, string prop) =>
        badge.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    public static string? GetEntityAttr(EntityState state, string key)
    {
        if (state.Attributes.TryGetValue(key, out var val) && val != null)
        {
            return val switch
            {
                string s => s,
                long l => l.ToString(CultureInfo.InvariantCulture),
                double d => d.ToString(CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                _ => val.ToString()
            };
        }
        return null;
    }

    // =============================================
    // INLINE ICON TEXT DRAWING
    // =============================================

    private static readonly Regex InlineIconPattern = new(@":fa-([a-z0-9-]+):", RegexOptions.Compiled);

    public float DrawTextWithInlineIcons(Image<Rgba32> image, string text, Font font,
        Color textColor, Color iconColor, RectangleF bounds, int maxLines = int.MaxValue, float lineSpacing = 0)
    {
        if (!text.Contains(":fa-"))
            return TextDrawing.DrawWrappedTextEllipsis(image, text, font, textColor, bounds, maxLines, lineSpacing);

        var segments = ParseIconSegments(text);
        if (segments.Count == 0) return 0;

        var glyphHeight = TextMeasurer.MeasureSize("Ay", new TextOptions(font)).Height;
        var lineHeight = glyphHeight + lineSpacing;
        var iconSize = glyphHeight;
        var spaceWidth = TextMeasurer.MeasureSize(" ", new TextOptions(font)).Width;

        float x = bounds.X;
        float y = bounds.Y;
        int lineCount = 1;

        for (int si = 0; si < segments.Count; si++)
        {
            var seg = segments[si];
            if (y + lineHeight > bounds.Bottom + 1 || lineCount > maxLines) break;

            if (seg.IsIcon)
            {
                if (x > bounds.X && seg.LeadingSpace)
                    x += spaceWidth;

                if (x + iconSize > bounds.Right && x > bounds.X)
                {
                    x = bounds.X;
                    y += lineHeight;
                    lineCount++;
                    if (lineCount > maxLines || y + lineHeight > bounds.Bottom + 1) break;
                }
                DrawFaIcon(image, seg.Text, iconColor, new RectangleF(x, y + 1, iconSize, iconSize));
                x += iconSize;

                if (seg.TrailingSpace)
                    x += spaceWidth;
            }
            else
            {
                var words = seg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool firstWord = true;
                foreach (var word in words)
                {
                    bool needsSpace = x > bounds.X && (!firstWord || seg.LeadingSpace);
                    var wordStr = needsSpace ? " " + word : word;
                    var wordWidth = TextMeasurer.MeasureSize(wordStr, new TextOptions(font)).Width;

                    if (x + wordWidth > bounds.Right && x > bounds.X)
                    {
                        x = bounds.X;
                        y += lineHeight;
                        lineCount++;
                        if (lineCount > maxLines || y + lineHeight > bounds.Bottom + 1) break;
                        wordStr = word;
                        wordWidth = TextMeasurer.MeasureSize(wordStr, new TextOptions(font)).Width;
                    }

                    image.Mutate(ctx => ctx.DrawText(wordStr, font, textColor, new PointF(x, y)));
                    x += wordWidth;
                    firstWord = false;
                }
                if (lineCount > maxLines) break;
            }
        }

        return y - bounds.Y + lineHeight;
    }

    private static List<(string Text, bool IsIcon, bool LeadingSpace, bool TrailingSpace)> ParseIconSegments(string text)
    {
        var segments = new List<(string Text, bool IsIcon, bool LeadingSpace, bool TrailingSpace)>();
        int lastEnd = 0;

        foreach (Match m in InlineIconPattern.Matches(text))
        {
            if (m.Index > lastEnd)
            {
                var run = text[lastEnd..m.Index];
                segments.Add((run, false, false, false));
            }

            bool leadingSpace = m.Index > 0 && char.IsWhiteSpace(text[m.Index - 1]);
            bool trailingSpace = m.Index + m.Length < text.Length && char.IsWhiteSpace(text[m.Index + m.Length]);
            segments.Add(($"fa-{m.Groups[1].Value}", true, leadingSpace, trailingSpace));
            lastEnd = m.Index + m.Length;
        }

        if (lastEnd < text.Length)
            segments.Add((text[lastEnd..], false, false, false));

        return segments;
    }

    // =============================================
    // DITHERING
    // =============================================

    public static void DitherRegion(Image<Rgba32> image, LayoutConfig layout, RectangleF region)
    {
        int rx = Math.Max(0, (int)region.X);
        int ry = Math.Max(0, (int)region.Y);
        int rw = Math.Min(image.Width - rx, Math.Max(1, (int)Math.Ceiling(region.Width)));
        int rh = Math.Min(image.Height - ry, Math.Max(1, (int)Math.Ceiling(region.Height)));
        if (rw <= 0 || rh <= 0) return;

        var paletteColors = layout.ColorScheme.Palette
            .Select(hex => ColorUtils.ParseColor(hex))
            .ToArray();
        if (paletteColors.Length == 0) return;

        using var sub = image.Clone(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(rx, ry, rw, rh)));
        sub.Mutate(ctx => ctx.Quantize(new PaletteQuantizer(
            new ReadOnlyMemory<Color>(paletteColors),
            new QuantizerOptions { Dither = KnownDitherings.JarvisJudiceNinke })));
        image.Mutate(ctx => ctx.DrawImage(sub, new SixLabors.ImageSharp.Point(rx, ry), 1f));
    }

    // =============================================
    // PIXEL POSITION RESOLUTION
    // =============================================

    public static (double X, double Y, double Width, double Height) ResolvePixelPosition(WidgetPositionConfig pos, LayoutConfig layout)
    {
        var padding = layout.CanvasPadding;
        var gap = layout.WidgetGap;
        var cols = Math.Max(1, layout.GridCols);
        var rows = Math.Max(1, layout.GridRows);
        var innerWidth = Math.Max(0, layout.Width - padding * 2 - gap * (cols - 1));
        var innerHeight = Math.Max(0, layout.Height - padding * 2 - gap * (rows - 1));
        var cellWidth = (double)innerWidth / cols;
        var cellHeight = (double)innerHeight / rows;

        var x = padding + pos.X * (cellWidth + gap);
        var y = padding + pos.Y * (cellHeight + gap);
        var w = pos.W * cellWidth + (pos.W - 1) * gap;
        var h = pos.H * cellHeight + (pos.H - 1) * gap;

        return (Math.Round(x * 100) / 100, Math.Round(y * 100) / 100, Math.Round(w * 100) / 100, Math.Round(h * 100) / 100);
    }

    // =============================================
    // FORMAT HELPERS
    // =============================================

    public static string FormatEventDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return "";
        if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dateStr.Length == 10
                ? dt.ToString("ddd, MMM d", CultureInfo.InvariantCulture)
                : dt.ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);
        }
        return dateStr;
    }

    public static string FormatForecastTime(string? datetime, string mode)
    {
        if (string.IsNullOrEmpty(datetime)) return "";
        if (!DateTimeOffset.TryParse(datetime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return datetime;
        return mode switch
        {
            "hourly" => dt.ToString("HH:mm"),
            "weekly" => dt.ToString("ddd"),
            _ => dt.Day.ToString()
        };
    }

    public static string FormatCondition(string? condition)
    {
        if (string.IsNullOrEmpty(condition)) return "";
        return condition.ToLower() switch
        {
            "clear-night" => "Clear",
            "cloudy" => "Cloudy",
            "fog" => "Fog",
            "hail" => "Hail",
            "lightning" => "Storm",
            "lightning-rainy" => "Stormy",
            "partlycloudy" => "Pt. Cloudy",
            "pouring" => "Pouring",
            "rainy" => "Rainy",
            "snowy" => "Snowy",
            "snowy-rainy" => "Snowy Rain",
            "sunny" => "Sunny",
            "windy" => "Windy",
            "windy-variant" => "Windy",
            "exceptional" => "Exceptional",
            _ => condition
        };
    }

    public static string RoundNum(object? val)
    {
        if (val == null) return "";
        if (val is long l) return l.ToString();
        if (val is double d) return Math.Round(d).ToString(CultureInfo.InvariantCulture);
        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            return Math.Round(num).ToString(CultureInfo.InvariantCulture);
        return val.ToString() ?? "";
    }

    public static string RoundNumOneDecimal(object? val)
    {
        if (val == null) return "";
        if (val is double d) return Math.Round(d, 1).ToString(CultureInfo.InvariantCulture);
        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            return Math.Round(num, 1).ToString(CultureInfo.InvariantCulture);
        return val.ToString() ?? "";
    }

    // =============================================
    // GRAPH HELPERS
    // =============================================

    public static IPath BuildSmoothedPath(PointF[] pts, float tension)
    {
        var builder = new PathBuilder();
        builder.StartFigure();

        for (int i = 0; i < pts.Length - 1; i++)
        {
            var p0 = pts[Math.Max(i - 1, 0)];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = pts[Math.Min(i + 2, pts.Length - 1)];

            var cp1 = new PointF(
                p1.X + (p2.X - p0.X) * tension / 3f,
                p1.Y + (p2.Y - p0.Y) * tension / 3f);
            var cp2 = new PointF(
                p2.X - (p3.X - p1.X) * tension / 3f,
                p2.Y - (p3.Y - p1.Y) * tension / 3f);

            builder.AddCubicBezier(p1, cp1, cp2, p2);
        }

        return builder.Build();
    }
}
