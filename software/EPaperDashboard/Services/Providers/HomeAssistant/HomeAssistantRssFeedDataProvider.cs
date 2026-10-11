using System.Net.WebSockets;
using System.Text.Json;
using CSharpFunctionalExtensions;

namespace EPaperDashboard.Services.Providers.HomeAssistant;

/// <summary>
/// Home Assistant implementation of <see cref="IRssFeedDataProvider"/>.
/// Fetches RSS feed entries via the Home Assistant WebSocket API.
/// </summary>
public class HomeAssistantRssFeedDataProvider(
    HomeAssistantConnectionService connection,
    ILogger<HomeAssistantRssFeedDataProvider> logger) : IRssFeedDataProvider
{
    private readonly HomeAssistantConnectionService _connection = connection;
    private readonly ILogger<HomeAssistantRssFeedDataProvider> _logger = logger;

    public async Task<Result<List<RssFeedEntry>, string>> FetchRssFeedEntriesAsync(string dashboardId, string feedEntityId, CancellationToken cancellationToken = default)
    {
        var connectionInfo = _connection.GetConnectionInfo(dashboardId);
        if (connectionInfo.IsFailure)
            return connectionInfo.Error;

        if (string.IsNullOrWhiteSpace(feedEntityId))
            return "Feed entity ID is required";

        var (hostUrl, token) = connectionInfo.Value;

        try
        {
            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, _connection.GetWebSocketPath(dashboardId), cancellationToken);

            var messageId = _connection.NextMessageId();
            await HomeAssistantConnectionService.SendMessageAsync(ws, new
            {
                id = messageId,
                type = "get_states"
            }, cancellationToken);

            var response = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, cancellationToken);
            _logger.LogDebug("HomeAssistant FetchRssFeedEntries raw response: {Response}", response);

            var json = JsonSerializer.Deserialize<JsonElement>(response);
            var entries = new List<RssFeedEntry>();

            if (json.TryGetProperty("success", out var success) && success.GetBoolean() &&
                json.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in result.EnumerateArray())
                {
                    var entityId = entity.TryGetProperty("entity_id", out var eid) ? eid.GetString() : null;

                    if (entityId == feedEntityId)
                    {
                        if (entity.TryGetProperty("attributes", out var attributes) &&
                            attributes.ValueKind == JsonValueKind.Object)
                        {
                            var entry = ParseRssFeedEntry(attributes);
                            if (entry != null)
                                entries.Add(entry);

                            _logger.LogDebug("Parsed RSS feed entry from event entity {EntityId}", feedEntityId);
                        }
                        else
                        {
                            _logger.LogWarning("Feed event entity {EntityId} found but has no attributes", feedEntityId);
                        }
                        break;
                    }
                }

                if (entries.Count == 0 && !result.EnumerateArray().Any(e => e.TryGetProperty("entity_id", out var eid) && eid.GetString() == feedEntityId))
                {
                    _logger.LogWarning("Feed event entity {EntityId} not found in states. Available event entities: {EventEntities}",
                        feedEntityId,
                        string.Join(", ", result.EnumerateArray()
                            .Where(e => e.TryGetProperty("entity_id", out var eid) && eid.GetString()?.StartsWith("event.") == true)
                            .Select(e => e.TryGetProperty("entity_id", out var eid) ? eid.GetString() : "unknown")
                        ));
                }
            }
            else
            {
                _logger.LogWarning("RSS feed entries fetch returned unsuccessful response or missing result property");
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); } catch { /* using disposes socket */ }
            return entries;
        }
        catch (OperationCanceledException) { throw; }
        catch (WebSocketException)
        {
            _logger.LogError("Unable to connect to Home Assistant WebSocket for RSS feed entries");
            return "Unable to connect to Home Assistant WebSocket. Please check the Host URL and ensure it's accessible.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch RSS feed entries from entity {EntityId}", feedEntityId);
            return $"Failed to fetch RSS feed entries: {ex.Message}";
        }
    }

    public async Task<Result<Dictionary<string, List<RssFeedEntry>>, string>> FetchAllRssFeedEntriesAsync(string dashboardId, CancellationToken cancellationToken = default)
    {
        var connectionInfo = _connection.GetConnectionInfo(dashboardId);
        if (connectionInfo.IsFailure)
            return connectionInfo.Error;

        var (hostUrl, token) = connectionInfo.Value;

        try
        {
            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, _connection.GetWebSocketPath(dashboardId), cancellationToken);

            await HomeAssistantConnectionService.SendMessageAsync(ws, new { id = 1, type = "get_states" }, cancellationToken);
            var response = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, cancellationToken);
            var json = JsonSerializer.Deserialize<JsonElement>(response);

            var result = new Dictionary<string, List<RssFeedEntry>>();

            if (json.TryGetProperty("success", out var success) && success.GetBoolean() &&
                json.TryGetProperty("result", out var statesResult) && statesResult.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in statesResult.EnumerateArray())
                {
                    var entityId = entity.TryGetProperty("entity_id", out var eid) ? eid.GetString() : null;
                    if (string.IsNullOrWhiteSpace(entityId))
                        continue;

                    var state = entity.TryGetProperty("state", out var s) ? s.GetString() : null;
                    if (state is "unavailable")
                        continue;

                    // Match feedreader event entities or sensor entities with "feed" in the name
                    var isFeedEntity = entityId.StartsWith("event.feedreader", StringComparison.OrdinalIgnoreCase)
                        || (entityId.StartsWith("sensor.", StringComparison.OrdinalIgnoreCase)
                            && entityId.Contains("feed", StringComparison.OrdinalIgnoreCase));

                    if (!isFeedEntity)
                        continue;

                    if (entity.TryGetProperty("attributes", out var attributes) &&
                        attributes.ValueKind == JsonValueKind.Object)
                    {
                        var entry = ParseRssFeedEntry(attributes);
                        if (entry != null)
                            result[entityId] = [entry];
                    }
                }
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); } catch { /* using disposes socket */ }
            _logger.LogDebug("Fetched RSS feed entries from {Count} entities for dashboard {DashboardId}", result.Count, dashboardId);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return $"Failed to discover RSS feed entities: {ex.Message}";
        }
    }

    private RssFeedEntry? ParseRssFeedEntry(JsonElement attributesElement)
    {
        try
        {
            if (attributesElement.ValueKind != JsonValueKind.Object)
                return null;

            var title = attributesElement.TryGetProperty("title", out var titleProp)
                ? titleProp.GetString() ?? string.Empty
                : string.Empty;

            var link = attributesElement.TryGetProperty("link", out var linkProp)
                ? linkProp.GetString() ?? string.Empty
                : string.Empty;

            string? description = null;
            if (attributesElement.TryGetProperty("description", out var descProp))
                description = descProp.GetString();
            else if (attributesElement.TryGetProperty("summary", out var summaryProp))
                description = summaryProp.GetString();
            else if (attributesElement.TryGetProperty("content", out var contentProp))
                description = contentProp.GetString();

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(link))
            {
                _logger.LogWarning("Skipping RSS entry with missing title and link");
                return null;
            }

            return new RssFeedEntry
            {
                Title = title,
                Link = link,
                Published = null,
                Summary = description
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse RSS feed entry from event attributes");
            return null;
        }
    }
}
