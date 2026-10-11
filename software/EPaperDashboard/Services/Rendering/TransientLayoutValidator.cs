using System.Text.Json;

namespace EPaperDashboard.Services.Rendering;

/// <summary>
/// Applies resource and geometry limits before an unpersisted browser layout reaches the renderer.
/// </summary>
public static class TransientLayoutValidator
{
    private const int MaxWidgets = 500;
    private const int MaxEditableElements = 100;

    public static string? Validate(Models.LayoutConfig? layout)
    {
        if (layout is null)
            return "A layout is required.";
        if (layout.Width is < 1 or > 4096 || layout.Height is < 1 or > 4096)
            return "Dashboard dimensions must be between 1 and 4096 pixels.";
        if (layout.GridCols is < 1 or > 100 || layout.GridRows is < 1 or > 100)
            return "Dashboard grid dimensions must be between 1 and 100.";
        if (layout.Widgets.Count > MaxWidgets)
            return $"Dashboard cannot contain more than {MaxWidgets} widgets.";
        if (layout.CanvasPadding is < 0 or > 2048
            || layout.WidgetGap is < 0 or > 256
            || layout.WidgetBorder is < 0 or > 256
            || layout.WidgetPadding is < 0 or > 256)
            return "Dashboard spacing values are outside the supported range.";
        if (layout.TitleFontSize is < 0 or > 256 || layout.TextFontSize is < 0 or > 256
            || layout.TitleFontWeight is < 0 or > 1000 || layout.TextFontWeight is < 0 or > 1000)
            return "Dashboard typography values are outside the supported range.";

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var widget in layout.Widgets)
        {
            if (string.IsNullOrWhiteSpace(widget.Id) || string.IsNullOrWhiteSpace(widget.Type))
                return "Every widget must have an ID and type.";
            if (!ids.Add(widget.Id))
                return $"Widget ID '{widget.Id}' is duplicated.";
            var position = widget.Position;
            if (position.X < 0 || position.Y < 0 || position.W < 1 || position.H < 1
                || position.X + position.W > layout.GridCols
                || position.Y + position.H > layout.GridRows)
                return $"Widget '{widget.Id}' is outside the dashboard grid.";
            if (widget.FontOverrides is { } fonts
                && (fonts.TitleFontSize is < 1 or > 256 || fonts.TextFontSize is < 1 or > 256))
                return $"Widget '{widget.Id}' font sizes are outside the supported range.";
            if (widget.Name is { Length: > 200 })
                return $"Widget '{widget.Id}' name is too long.";
            if (widget.Config.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
                return $"Widget '{widget.Id}' configuration must be an object.";

            var editableError = ValidateEditableConfig(widget);
            if (editableError is not null) return editableError;
        }

        return null;
    }

    private static string? ValidateEditableConfig(Models.WidgetConfig widget)
    {
        if (widget.Config.ValueKind != JsonValueKind.Object) return null;

        if (widget.Type == "header")
        {
            var error = ValidateRectangle(widget.Config, "titleX", "titleY", "titleW", "titleH");
            if (error is not null) return $"Widget '{widget.Id}' title {error}";
            return ValidateArray(widget, "badges");
        }

        return widget.Type == "weather" ? ValidateArray(widget, "items") : null;
    }

    private static string? ValidateArray(Models.WidgetConfig widget, string property)
    {
        if (!widget.Config.TryGetProperty(property, out var array)) return null;
        if (array.ValueKind != JsonValueKind.Array)
            return $"Widget '{widget.Id}' {property} must be an array.";
        if (array.GetArrayLength() > MaxEditableElements)
            return $"Widget '{widget.Id}' cannot contain more than {MaxEditableElements} {property}.";

        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                return $"Widget '{widget.Id}' {property} entry {index} must be an object.";
            var error = ValidateRectangle(element, "x", "y", "w", "h");
            if (error is not null)
                return $"Widget '{widget.Id}' {property} entry {index} {error}";
            index++;
        }
        return null;
    }

    private static string? ValidateRectangle(
        JsonElement element,
        string xName,
        string yName,
        string widthName,
        string heightName)
    {
        var x = ReadNumber(element, xName);
        var y = ReadNumber(element, yName);
        var width = ReadNumber(element, widthName);
        var height = ReadNumber(element, heightName);
        if (x.Invalid || y.Invalid || width.Invalid || height.Invalid)
            return "contains a non-numeric position.";

        if (x.Value is < 0 or > 100 || y.Value is < 0 or > 100
            || width.Value is <= 0 or > 100 || height.Value is <= 0 or > 100
            || x.Value.HasValue && width.Value.HasValue && x.Value + width.Value > 100.001
            || y.Value.HasValue && height.Value.HasValue && y.Value + height.Value > 100.001)
            return "position must fit within its widget content area.";
        return null;
    }

    private static (double? Value, bool Invalid) ReadNumber(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return (null, false);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
            || double.IsNaN(number) || double.IsInfinity(number))
            return (null, true);
        return (number, false);
    }
}
