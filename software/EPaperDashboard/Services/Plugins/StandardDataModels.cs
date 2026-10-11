using System.Text.Json;
namespace EPaperDashboard.Services;

public record EntityState
{
    public string EntityId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public Dictionary<string, object?> Attributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public record WeatherForecast
{
    public string? Datetime { get; init; }
    public string? Condition { get; init; }
    public double? Temperature { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("templow")]
    public double? TempLow { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("precipitation_probability")]
    public double? PrecipitationProbability { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("wind_speed")]
    public double? WindSpeed { get; init; }
}

public record TodoItem
{
    public string Summary { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Uid { get; init; } = string.Empty;
}

public record CalendarEvent
{
    /// <summary>
    /// Event unique identifier
    /// </summary>
    public string Uid { get; init; } = string.Empty;

    /// <summary>
    /// Event title/summary
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Event description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Event location
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Event start time (ISO 8601 format)
    /// </summary>
    public string Start { get; init; } = string.Empty;

    /// <summary>
    /// Event end time (ISO 8601 format)
    /// </summary>
    public string? End { get; init; }

    /// <summary>
    /// Whether this is an all-day event
    /// </summary>
    public bool AllDay { get; init; }

    /// <summary>
    /// Recurrence rule if the event repeats
    /// </summary>
    public string? RecurrenceRule { get; init; }
}

public record RssFeedEntry
{
    /// <summary>
    /// Entry title
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Entry link/URL
    /// </summary>
    public string Link { get; init; } = string.Empty;

    /// <summary>
    /// Entry publication date
    /// </summary>
    public string? Published { get; init; }

    /// <summary>
    /// Entry summary/description
    /// </summary>
    public string? Summary { get; init; }
}
/// <summary>
/// Represents a historical state entry supplied by a data provider.
/// Includes timestamp and numeric value for graphing purposes.
/// </summary>
public record HistoryState
{
    /// <summary>
    /// The state value as a string (may be numeric, "on"/"off", etc.)
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// The state value parsed as a numeric value for graphing (0 if not numeric)
    /// </summary>
    public double NumericValue { get; init; }

    /// <summary>
    /// Timestamp when this state change occurred
    /// </summary>
    public DateTime LastChanged { get; init; }

    /// <summary>
    /// Entity attributes at this point in time
    /// </summary>
    public Dictionary<string, JsonElement> Attributes { get; init; } = new();
}
