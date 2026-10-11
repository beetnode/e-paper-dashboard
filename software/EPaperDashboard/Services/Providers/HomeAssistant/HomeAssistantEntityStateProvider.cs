using System.Net.WebSockets;
using System.Text.Json;
using CSharpFunctionalExtensions;

namespace EPaperDashboard.Services.Providers.HomeAssistant;

/// <summary>
/// Home Assistant implementation of <see cref="IEntityStateProvider"/>.
/// Fetches entity states via the Home Assistant WebSocket API.
/// </summary>
public class HomeAssistantEntityStateProvider(
    HomeAssistantConnectionService connection,
    ILogger<HomeAssistantEntityStateProvider> logger) : IEntityStateProvider
{
    private readonly HomeAssistantConnectionService _connection = connection;
    private readonly ILogger<HomeAssistantEntityStateProvider> _logger = logger;

    /// <summary>
    /// Domains relevant for dashboard display: sensors, device states, persons, etc.
    /// Excludes internal/automation domains that have no widget representation.
    /// </summary>
    private static readonly HashSet<string> RelevantDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "sensor", "binary_sensor", "person", "sun",
        "climate", "light", "switch", "lock", "cover", "fan"
    };

    public async Task<Result<List<EntityState>, string>> FetchAllEntityStatesAsync(string dashboardId, CancellationToken cancellationToken = default)
    {
        var connectionInfo = _connection.GetConnectionInfo(dashboardId);
        if (connectionInfo.IsFailure)
        {
            return connectionInfo.Error;
        }

        var (hostUrl, token) = connectionInfo.Value;

        try
        {
            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, _connection.GetWebSocketPath(dashboardId), cancellationToken);

            await HomeAssistantConnectionService.SendMessageAsync(ws, new
            {
                id = 1,
                type = "get_states"
            }, cancellationToken);

            var statesResponse = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, cancellationToken);
            var statesResult = JsonSerializer.Deserialize<JsonElement>(statesResponse);

            var entityStates = new List<EntityState>();

            if (statesResult.TryGetProperty("success", out var success) && success.GetBoolean() &&
                statesResult.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in result.EnumerateArray())
                {
                    var entityId = entity.TryGetProperty("entity_id", out var eid) ? eid.GetString() : null;
                    if (string.IsNullOrWhiteSpace(entityId))
                    {
                        continue;
                    }

                    var domain = entityId.Split('.')[0];
                    if (!RelevantDomains.Contains(domain))
                    {
                        continue;
                    }

                    var state = entity.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : string.Empty;
                    if (state is "unavailable" or "unknown")
                    {
                        continue;
                    }

                    var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (entity.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var attr in attrs.EnumerateObject())
                        {
                            attributes[attr.Name] = HomeAssistantConnectionService.ExtractJsonValue(attr.Value);
                        }
                    }

                    entityStates.Add(new EntityState
                    {
                        EntityId = entityId,
                        State = state ?? string.Empty,
                        Attributes = attributes
                    });
                }
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); } catch { /* using disposes socket */ }
            _logger.LogDebug("Fetched {Count} relevant entity states for dashboard {DashboardId}", entityStates.Count, dashboardId);
            return entityStates;
        }
        catch (OperationCanceledException) { throw; }
        catch (WebSocketException)
        {
            return "Unable to connect to Home Assistant WebSocket. Please check the Host URL and ensure it's accessible.";
        }
        catch (Exception ex)
        {
            return $"Failed to fetch entity states: {ex.Message}";
        }
    }

    public async Task<Result<List<EntityState>, string>> FetchEntityStatesAsync(string dashboardId, string[] entityIds, CancellationToken cancellationToken = default)
    {
        var connectionInfo = _connection.GetConnectionInfo(dashboardId);
        if (connectionInfo.IsFailure)
            return connectionInfo.Error;

        var requestedIds = new HashSet<string>(entityIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        if (requestedIds.Count == 0)
            return Result.Success<List<EntityState>, string>(new List<EntityState>());

        var (hostUrl, token) = connectionInfo.Value;

        try
        {
            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, _connection.GetWebSocketPath(dashboardId), cancellationToken);

            await HomeAssistantConnectionService.SendMessageAsync(ws, new
            {
                id = 1,
                type = "get_states"
            }, cancellationToken);

            var statesResponse = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, cancellationToken);
            var statesResult = JsonSerializer.Deserialize<JsonElement>(statesResponse);

            var entityStates = new List<EntityState>();

            if (statesResult.TryGetProperty("success", out var success) && success.GetBoolean() &&
                statesResult.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in result.EnumerateArray())
                {
                    var entityId = entity.TryGetProperty("entity_id", out var eid) ? eid.GetString() : null;
                    if (string.IsNullOrWhiteSpace(entityId) || !requestedIds.Contains(entityId))
                        continue;

                    var state = entity.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : string.Empty;
                    var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                    if (entity.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var attr in attrs.EnumerateObject())
                        {
                            attributes[attr.Name] = HomeAssistantConnectionService.ExtractJsonValue(attr.Value);
                        }
                    }

                    entityStates.Add(new EntityState
                    {
                        EntityId = entityId,
                        State = state ?? string.Empty,
                        Attributes = attributes
                    });
                }
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); } catch { /* using disposes socket */ }
            return entityStates;
        }
        catch (OperationCanceledException) { throw; }
        catch (WebSocketException)
        {
            return "Unable to connect to Home Assistant WebSocket. Please check the Host URL and ensure it's accessible.";
        }
        catch (Exception ex)
        {
            return $"Failed to fetch entity states: {ex.Message}";
        }
    }
}
