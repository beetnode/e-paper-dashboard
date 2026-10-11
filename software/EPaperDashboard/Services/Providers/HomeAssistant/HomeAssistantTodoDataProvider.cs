using System.Net.WebSockets;
using System.Text.Json;
using CSharpFunctionalExtensions;

namespace EPaperDashboard.Services.Providers.HomeAssistant;

/// <summary>
/// Home Assistant implementation of <see cref="ITodoDataProvider"/>.
/// Fetches todo items via the Home Assistant WebSocket API.
/// </summary>
public class HomeAssistantTodoDataProvider(
    HomeAssistantConnectionService connection,
    ILogger<HomeAssistantTodoDataProvider> logger) : ITodoDataProvider
{
    private readonly HomeAssistantConnectionService _connection = connection;
    private readonly ILogger<HomeAssistantTodoDataProvider> _logger = logger;

    public async Task<Result<List<TodoItem>, string>> FetchTodoItemsAsync(string dashboardId, string todoEntityId, CancellationToken cancellationToken = default)
    {
        var connectionInfo = _connection.GetConnectionInfo(dashboardId);
        if (connectionInfo.IsFailure)
            return connectionInfo.Error;

        var (hostUrl, token) = connectionInfo.Value;

        try
        {
            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, _connection.GetWebSocketPath(dashboardId), cancellationToken);

            var messageId = _connection.NextMessageId();
            await HomeAssistantConnectionService.SendMessageAsync(ws, new
            {
                id = messageId,
                type = "call_service",
                domain = "todo",
                service = "get_items",
                service_data = new
                {
                    entity_id = todoEntityId
                },
                return_response = true
            }, cancellationToken);

            var response = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, cancellationToken);
            _logger.LogDebug("HomeAssistant FetchTodoItems raw response: {Response}", response);

            var json = JsonSerializer.Deserialize<JsonElement>(response);
            var items = new List<TodoItem>();

            if (json.TryGetProperty("success", out var success) && success.GetBoolean() &&
                json.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
            {
                JsonElement itemsArray = default;
                bool foundItems = false;

                if (result.TryGetProperty("response", out var responseObj) && responseObj.ValueKind == JsonValueKind.Object)
                {
                    if (responseObj.TryGetProperty(todoEntityId, out var entityObj) && entityObj.ValueKind == JsonValueKind.Object &&
                        entityObj.TryGetProperty("items", out itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
                    {
                        foundItems = true;
                        _logger.LogDebug("Found items at result.response.{EntityId}.items", todoEntityId);
                    }
                }

                if (!foundItems && result.TryGetProperty(todoEntityId, out var entityObj2) && entityObj2.ValueKind == JsonValueKind.Object &&
                    entityObj2.TryGetProperty("items", out itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
                {
                    foundItems = true;
                    _logger.LogDebug("Found items at result.{EntityId}.items", todoEntityId);
                }

                if (!foundItems && result.TryGetProperty("items", out itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
                {
                    foundItems = true;
                    _logger.LogDebug("Found items at result.items");
                }

                if (foundItems)
                {
                    foreach (var item in itemsArray.EnumerateArray())
                    {
                        var summary = item.TryGetProperty("summary", out var s) ? s.GetString() : null;
                        var status = item.TryGetProperty("status", out var st) ? st.GetString() : null;
                        var uid = item.TryGetProperty("uid", out var u) ? u.GetString() : null;
                        items.Add(new TodoItem
                        {
                            Summary = summary ?? string.Empty,
                            Status = status ?? string.Empty,
                            Uid = uid ?? string.Empty
                        });
                    }
                    _logger.LogDebug("Parsed {Count} todo items from entity {EntityId}", items.Count, todoEntityId);
                }
                else
                {
                    _logger.LogWarning("Could not find items array in todo.get_items response for entity {EntityId}. Response was: {Response}", todoEntityId, response);
                }
            }
            else
            {
                _logger.LogWarning("Todo items fetch returned unsuccessful response or missing result property. Response was: {Response}", response);
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); } catch { /* using disposes socket */ }
            return items;
        }
        catch (OperationCanceledException) { throw; }
        catch (WebSocketException)
        {
            return "Unable to connect to Home Assistant WebSocket. Please check the Host URL and ensure it's accessible.";
        }
        catch (Exception ex)
        {
            return $"Failed to fetch todo items: {ex.Message}";
        }
    }

    public async Task<Result<Dictionary<string, List<TodoItem>>, string>> FetchAllTodoItemsAsync(string dashboardId, CancellationToken cancellationToken = default)
    {
        var entityIds = await DiscoverEntitiesAsync(dashboardId, "todo", cancellationToken);
        if (entityIds.IsFailure)
        {
            return entityIds.Error;
        }

        var result = new Dictionary<string, List<TodoItem>>();
        foreach (var entityId in entityIds.Value)
        {
            var itemsResult = await FetchTodoItemsAsync(dashboardId, entityId, cancellationToken);
            if (itemsResult.IsSuccess && itemsResult.Value.Count > 0)
            {
                result[entityId] = itemsResult.Value;
            }
        }

        _logger.LogDebug("Fetched todo items from {Count} entities for dashboard {DashboardId}", result.Count, dashboardId);
        return result;
    }

    private async Task<Result<List<string>, string>> DiscoverEntitiesAsync(string dashboardId, string domain, CancellationToken cancellationToken)
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

            await HomeAssistantConnectionService.SendMessageAsync(ws, new { id = 1, type = "get_states" }, cancellationToken);
            var response = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, cancellationToken);
            var json = JsonSerializer.Deserialize<JsonElement>(response);

            var entityIds = new List<string>();
            if (json.TryGetProperty("success", out var success) && success.GetBoolean() &&
                json.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in result.EnumerateArray())
                {
                    var entityId = entity.TryGetProperty("entity_id", out var eid) ? eid.GetString() : null;
                    if (entityId != null && entityId.StartsWith(domain + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        var state = entity.TryGetProperty("state", out var s) ? s.GetString() : null;
                        if (state is not "unavailable")
                        {
                            entityIds.Add(entityId);
                        }
                    }
                }
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); } catch { /* using disposes socket */ }
            return entityIds;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return $"Failed to discover {domain} entities: {ex.Message}";
        }
    }
}
