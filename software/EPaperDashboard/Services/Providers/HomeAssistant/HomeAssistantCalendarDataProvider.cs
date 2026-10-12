using EPaperDashboard.Models;
using System.Net.WebSockets;
using System.Text.Json;
using CSharpFunctionalExtensions;

namespace EPaperDashboard.Services.Providers.HomeAssistant;

/// <summary>
/// Home Assistant implementation of <see cref="ICalendarDataProvider"/>.
/// Fetches calendar events via the Home Assistant WebSocket API.
/// </summary>
public class HomeAssistantCalendarDataProvider(
    HomeAssistantConnectionService connection,
    ILogger<HomeAssistantCalendarDataProvider> logger)
{
    private readonly HomeAssistantConnectionService _connection = connection;
    private readonly ILogger<HomeAssistantCalendarDataProvider> _logger = logger;

    public async Task<Result<List<CalendarEvent>, string>> FetchCalendarEventsAsync(ProviderConnection homeAssistant, string calendarEntityId, int durationHours = 168, CancellationToken cancellationToken = default)
    {
        var connectionInfo = _connection.GetConnectionInfo(homeAssistant);
        if (connectionInfo.IsFailure)
            return connectionInfo.Error;

        if (string.IsNullOrWhiteSpace(calendarEntityId))
            return "Calendar entity ID is required";

        var (hostUrl, token) = connectionInfo.Value;

        try
        {
            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, connectionInfo.Value.WebSocketPath, cancellationToken);

            var messageId = _connection.NextMessageId();
            var now = DateTime.UtcNow;
            var endTime = now.AddHours(durationHours);

            await HomeAssistantConnectionService.SendMessageAsync(ws, new
            {
                id = messageId,
                type = "call_service",
                domain = "calendar",
                service = "get_events",
                service_data = new
                {
                    start_date_time = now.ToString("O"),
                    end_date_time = endTime.ToString("O")
                },
                target = new
                {
                    entity_id = calendarEntityId
                },
                return_response = true
            }, cancellationToken);

            var response = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, cancellationToken);
            _logger.LogDebug("HomeAssistant FetchCalendarEvents raw response: {Response}", response);

            var json = JsonSerializer.Deserialize<JsonElement>(response);
            var events = new List<CalendarEvent>();

            if (json.TryGetProperty("success", out var success) && success.GetBoolean() &&
                json.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
            {
                JsonElement eventsArray = default;
                bool foundEvents = false;

                if (result.TryGetProperty("response", out var responseObj) && responseObj.ValueKind == JsonValueKind.Object)
                {
                    if (responseObj.TryGetProperty(calendarEntityId, out var entityObj) &&
                        entityObj.ValueKind == JsonValueKind.Object &&
                        entityObj.TryGetProperty("events", out eventsArray) &&
                        eventsArray.ValueKind == JsonValueKind.Array)
                    {
                        foundEvents = true;
                        _logger.LogDebug("Found events at result.response.{EntityId}.events", calendarEntityId);
                    }
                }

                if (!foundEvents && result.TryGetProperty(calendarEntityId, out var entityObj2) &&
                    entityObj2.ValueKind == JsonValueKind.Object &&
                    entityObj2.TryGetProperty("events", out eventsArray) &&
                    eventsArray.ValueKind == JsonValueKind.Array)
                {
                    foundEvents = true;
                    _logger.LogDebug("Found events at result.{EntityId}.events", calendarEntityId);
                }

                if (!foundEvents && result.TryGetProperty("events", out eventsArray) &&
                    eventsArray.ValueKind == JsonValueKind.Array)
                {
                    foundEvents = true;
                    _logger.LogDebug("Found events at result.events");
                }

                if (foundEvents)
                {
                    foreach (var eventElement in eventsArray.EnumerateArray())
                    {
                        var calendarEvent = ParseCalendarEvent(eventElement);
                        if (calendarEvent != null)
                            events.Add(calendarEvent);
                    }

                    events = events.OrderBy(e => e.Start).ToList();
                    _logger.LogDebug("Parsed {Count} calendar events from entity {EntityId} between {Start} and {End}", events.Count, calendarEntityId, now, endTime);
                }
                else
                {
                    _logger.LogWarning("Could not find events array in calendar.get_events response for entity {EntityId}. Response was: {Response}", calendarEntityId, response);
                }
            }
            else
            {
                _logger.LogWarning("Calendar events fetch returned unsuccessful response. Response was: {Response}", response);
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); } catch { /* using disposes socket */ }
            return events;
        }
        catch (OperationCanceledException) { throw; }
        catch (WebSocketException)
        {
            _logger.LogError("Unable to connect to Home Assistant WebSocket for calendar events");
            return "Unable to connect to Home Assistant WebSocket. Please check the Host URL and ensure it's accessible.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch calendar events from entity {EntityId}", calendarEntityId);
            return $"Failed to fetch calendar events: {ex.Message}";
        }
    }

    private CalendarEvent? ParseCalendarEvent(JsonElement eventElement)
    {
        try
        {
            if (eventElement.ValueKind != JsonValueKind.Object)
                return null;

            string? start = null;
            if (eventElement.TryGetProperty("start", out var startProp))
                start = ExtractDateTimeString(startProp);

            if (string.IsNullOrWhiteSpace(start))
            {
                _logger.LogWarning("Skipping calendar event with missing start time");
                return null;
            }

            string? end = null;
            if (eventElement.TryGetProperty("end", out var endProp))
                end = ExtractDateTimeString(endProp);

            string summary = string.Empty;
            if (eventElement.TryGetProperty("summary", out var summaryProp))
                summary = summaryProp.GetString() ?? string.Empty;

            string? description = null;
            if (eventElement.TryGetProperty("description", out var descProp))
                description = descProp.GetString();

            string? location = null;
            if (eventElement.TryGetProperty("location", out var locProp))
                location = locProp.GetString();

            string uid = string.Empty;
            if (eventElement.TryGetProperty("uid", out var uidProp))
                uid = uidProp.GetString() ?? Guid.NewGuid().ToString();
            else
                uid = Guid.NewGuid().ToString();

            bool allDay = false;
            if (eventElement.TryGetProperty("all_day", out var allDayProp))
                allDay = allDayProp.GetBoolean();

            if (!allDay && !string.IsNullOrWhiteSpace(start) && start.Length == 10 && start[4] == '-' && start[7] == '-')
                allDay = true;

            string? recurrenceRule = null;
            if (eventElement.TryGetProperty("rrule", out var rRuleProp))
                recurrenceRule = rRuleProp.GetString();

            return new CalendarEvent
            {
                Uid = uid,
                Summary = summary,
                Description = description,
                Location = location,
                Start = start,
                End = end,
                AllDay = allDay,
                RecurrenceRule = recurrenceRule
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse calendar event element");
            return null;
        }
    }



    private static string? ExtractDateTimeString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Object =>
                element.TryGetProperty("__type", out var typeElement) && typeElement.GetString() == "ISO8601_STR" &&
                element.TryGetProperty("isoformat", out var isoProp) ? isoProp.GetString() : null,
            _ => null
        };
    }
}
