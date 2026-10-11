using System.Text.Json;
using EPaperDashboard.Models.Rendering;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;
using RectangleF = SixLabors.ImageSharp.RectangleF;

namespace EPaperDashboard.Services.Rendering.Widgets;

public sealed class GraphWidgetRenderer(RenderingUtilities utils) : IWidgetRenderer
{
    public string WidgetType => "graph";

    public Task RenderAsync(Image<Rgba32> image, WidgetConfigEntry widget, LayoutConfig layout, SsrData data, RectangleF contentRect, CancellationToken cancellationToken = default)
    {
        var ctx = WidgetRenderContext.Create(widget, layout);
        var textColor = ctx.TextColor;
        var titleColor = ctx.TitleColor;
        var textFontSize = ctx.TextFontSize;
        var gridColorStr = widget.ColorOverrides?.WidgetBorderColor ?? layout.ColorScheme.WidgetBorderColor;

        contentRect = WidgetFrameRenderer.DrawOptionalCenteredTitle(image, widget, layout, utils, contentRect);

        var plotType = RenderingUtilities.GetStringProp(widget.Config, "plotType") ?? "line";
        var lineWidth = RenderingUtilities.GetIntProp(widget.Config, "lineWidth") ?? 2;
        var barWidth = RenderingUtilities.GetIntProp(widget.Config, "barWidth") ?? 2;
        var periodHours = GetPeriodHours(RenderingUtilities.GetStringProp(widget.Config, "period"));

        var seriesList = new List<(string EntityId, string Label, string Color)>();
        if (widget.Config.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            int idx = 0;
            foreach (var s in series.EnumerateArray())
            {
                var sEntityId = RenderingUtilities.GetStringProp(s, "entityId") ?? "";
                var sLabel = RenderingUtilities.GetStringProp(s, "label") is { Length: > 0 } label
                    ? label
                    : RenderingUtilities.GetSourceDisplayName(sEntityId, data);
                var sColor = RenderingUtilities.GetStringProp(s, "color") ?? ColorUtils.GetDefaultSeriesColor(layout.ColorScheme, idx);
                if (!string.IsNullOrEmpty(sEntityId))
                    seriesList.Add((sEntityId, sLabel, sColor));
                idx++;
            }
        }

        var availableStates = seriesList
            .Select(seriesEntry => seriesEntry.EntityId)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(entityId => data.HistoryData.GetValueOrDefault(entityId) ?? [])
            .ToList();
        var latestTimestamp = availableStates.Count > 0
            ? availableStates.Max(state => state.LastChanged)
            : DateTime.MinValue;
        var cutoff = latestTimestamp.AddHours(-periodHours);
        var historyByEntity = seriesList
            .Select(seriesEntry => seriesEntry.EntityId)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                entityId => entityId,
                entityId => (data.HistoryData.GetValueOrDefault(entityId) ?? [])
                    .Where(state => state.LastChanged >= cutoff)
                    .ToList(),
                StringComparer.Ordinal);

        var hasData = historyByEntity.Values.Any(states => states.Count > 0);
        if (!hasData)
        {
            TextDrawing.DrawCenteredText(image, "Graph", utils.GetFont(textFontSize), titleColor, contentRect);
            return Task.CompletedTask;
        }

        // Collect all data points
        var allValues = new List<double>();
        var allTimestamps = new List<DateTime>();
        foreach (var (entityId, _, _) in seriesList)
        {
            if (!historyByEntity.TryGetValue(entityId, out var states)) continue;
            foreach (var s in states)
            {
                allValues.Add(s.NumericValue);
                allTimestamps.Add(s.LastChanged);
            }
        }

        if (allValues.Count == 0) return Task.CompletedTask;

        var minVal = allValues.Min();
        var maxVal = allValues.Max();
        if (Math.Abs(maxVal - minVal) < 0.001) { minVal -= 1; maxVal += 1; }
        var valRange = maxVal - minVal;

        var minTime = allTimestamps.Min();
        var maxTime = allTimestamps.Max();
        var timeRange = (maxTime - minTime).TotalSeconds;
        if (timeRange < 1) timeRange = 1;

        // Reserve space for legend below X-axis labels when multiple series exist
        var showLegend = seriesList.Count > 1;
        var legendBoxSize = 8f;
        var legendPadding = 8f;
        var legendRowHeight = showLegend ? legendBoxSize + 6 : 0f;

        var padL = Math.Max(35, textFontSize * 4);
        var padR = 10f;
        var padT = 8f;
        var xAxisLabelHeight = Math.Max(16, textFontSize + 4);
        var padB = xAxisLabelHeight + legendRowHeight + 4;
        var plotW = contentRect.Width - padL - padR;
        var plotH = contentRect.Height - padT - padB;
        if (plotW <= 0 || plotH <= 0) return Task.CompletedTask;
        var originX = contentRect.X + padL;
        var originY = contentRect.Y + padT;

        // Match frontend Chart.js grid: `${widgetBorderColor}20` ≈ 12.5% alpha
        var gridColor = ColorUtils.WithOpacity(ColorUtils.ParseColor(gridColorStr), 0.125f);
        var labelFont = utils.GetFont(Math.Max(8, textFontSize - 2));

        // Grid lines
        var yAxisFormat = valRange < 1 ? "F2" : valRange < 10 ? "F1" : "F0";
        image.Mutate(ctx =>
        {
            for (int i = 0; i <= 3; i++)
            {
                var y = originY + plotH * i / 3f;
                ctx.DrawLine(gridColor, 0.5f, new PointF(originX, y), new PointF(originX + plotW, y));

                var val = maxVal - (valRange * i / 3.0);
                var labelRect = new RectangleF(contentRect.X, y - textFontSize / 2f, padL - 4, textFontSize);
                TextDrawing.DrawTextAligned(ctx, image, val.ToString(yAxisFormat), labelFont, textColor, labelRect, HorizontalAlignment.Right);
            }

            // X axis
            ctx.DrawLine(gridColor, 0.5f,
                new PointF(originX, originY + plotH),
                new PointF(originX + plotW, originY + plotH));
        });

        // X axis labels — positioned just below the plot area
        var xLabelY = originY + plotH + 3;
        for (int i = 0; i <= 4; i++)
        {
            var t = minTime.AddSeconds(timeRange * i / 4.0);
            var x = originX + plotW * i / 4f;
            var labelRect = new RectangleF(x - 20, xLabelY, 40, xAxisLabelHeight);
            TextDrawing.DrawTextCentered(image, t.ToString("HH:mm"), labelFont, textColor, labelRect);
        }

        // Render series
        foreach (var (entityId, label, color) in seriesList)
        {
            if (!historyByEntity.TryGetValue(entityId, out var states) || states.Count == 0) continue;
            var seriesColor = ColorUtils.ParseColor(color);
            var ordered = states.OrderBy(s => s.LastChanged).ToList();

            if (plotType == "bar")
            {
                var bw = barWidth > 0 ? barWidth * 3f : Math.Max(2, plotW / (ordered.Count + 1));
                image.Mutate(ctx =>
                {
                    foreach (var s in ordered)
                    {
                        var xFrac = (float)((s.LastChanged - minTime).TotalSeconds / timeRange);
                        var barX = originX + xFrac * plotW;
                        var yFrac = (float)((s.NumericValue - minVal) / valRange);
                        var barH = yFrac * plotH;
                        var barY = originY + plotH - barH;
                        ctx.Fill(seriesColor, new RectangularPolygon(barX, barY, bw, barH));
                    }
                });
            }
            else
            {
                // Line chart with Catmull-Rom smoothing (matches Chart.js tension: 0.3)
                if (ordered.Count < 2) continue;
                var points = ordered.Select(s =>
                {
                    var xFrac = (float)((s.LastChanged - minTime).TotalSeconds / timeRange);
                    var yFrac = (float)((s.NumericValue - minVal) / valRange);
                    return new PointF(originX + xFrac * plotW, originY + plotH - yFrac * plotH);
                }).ToArray();

                if (points.Length == 2)
                {
                    image.Mutate(ctx => ctx.DrawLine(seriesColor, lineWidth, points));
                }
                else
                {
                    var path = RenderingUtilities.BuildSmoothedPath(points, 0.3f);
                    image.Mutate(ctx => ctx.Draw(seriesColor, lineWidth, path));
                }
            }
        }

        // Legend — positioned below X-axis labels, within content bounds
        if (showLegend)
        {
            var legendFont = utils.GetFont(Math.Max(8, textFontSize - 2));
            // Place legend at the bottom of contentRect, leaving 2px margin
            var legendY = contentRect.Y + contentRect.Height - legendBoxSize - 2;

            var legendItems = seriesList.Select(s =>
            {
                var labelWidth = TextMeasurer.MeasureSize(s.Label, new TextOptions(legendFont)).Width;
                return (s.Label, s.Color, LabelWidth: labelWidth);
            }).ToList();
            var totalLegendWidth = legendItems.Sum(l => legendBoxSize + 4 + l.LabelWidth + legendPadding) - legendPadding;
            // Clamp legend to contentRect width
            var legendX = originX + Math.Max(0, (plotW - totalLegendWidth) / 2f);

            foreach (var (lLabel, lColor, lWidth) in legendItems)
            {
                var boxColor = ColorUtils.ParseColor(lColor);
                image.Mutate(ctx => ctx.Fill(boxColor, new RectangularPolygon(
                    legendX, legendY, legendBoxSize, legendBoxSize)));
                legendX += legendBoxSize + 4;
                var lRect = new RectangleF(legendX, legendY - 1, lWidth + 2, legendBoxSize + 2);
                TextDrawing.DrawTextEllipsis(image, lLabel, legendFont, textColor, lRect);
                legendX += lWidth + legendPadding;
            }
        }

        return Task.CompletedTask;
    }

    private static int GetPeriodHours(string? period) => period switch
    {
        "1h" => 1,
        "6h" => 6,
        "7d" => 168,
        "30d" => 720,
        _ => 24
    };
}
