
using System.Text.Json;
using System.Net.WebSockets;
using System.Text;
using CSharpFunctionalExtensions;
using EPaperDashboard.Models;

namespace EPaperDashboard.Services;

/// <summary>
/// Home Assistant service for HA-specific operations that are not widget data providers:
/// listing HA dashboards/views, listing entities, and sending notifications.
/// Widget data fetching is handled by per-widget provider implementations.
/// </summary>
public class HomeAssistantService(
    ILogger<HomeAssistantService> logger,
    HomeAssistantConnectionService connection)
{
    private readonly ILogger<HomeAssistantService> _logger = logger;
    private readonly HomeAssistantConnectionService _connection = connection;

    public async Task<Result<List<HassUrlInfo>, string>> FetchDashboards(string dashboardId)
    {
        var connectionInfo = _connection.GetConnectionInfo(dashboardId);
        if (connectionInfo.IsFailure)
            return connectionInfo.Error;

        var (hostUrl, token) = connectionInfo.Value;

        try
        {
            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, _connection.WebSocketPath);
            var results = await FetchAllDashboardViews(ws, hostUrl);

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);

            return results;
        }
        catch (WebSocketException)
        {
            return "Unable to connect to Home Assistant WebSocket. Please check the Host URL and ensure it's accessible.";
        }
        catch (Exception ex)
        {
            return $"Failed to fetch dashboards: {ex.Message}";
        }
    }

    public async Task<Result<List<HassEntity>, string>> FetchEntities(string dashboardId, CancellationToken cancellationToken = default)
    {
        var connectionInfo = _connection.GetConnectionInfo(dashboardId);
        if (connectionInfo.IsFailure)
            return connectionInfo.Error;

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

            var entities = new List<HassEntity>();
            
            if (statesResult.TryGetProperty("success", out var success) && success.GetBoolean() &&
                statesResult.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in result.EnumerateArray())
                {
                    var entityId = entity.TryGetProperty("entity_id", out var eid) ? eid.GetString() : null;
                    var friendlyName = string.Empty;
                    string? deviceClass = null;
                    string? unitOfMeasurement = null;
                    string? icon = null;
                    string? state = null;
                    int? supportedFeatures = null;
                    
                    if (entity.TryGetProperty("state", out var stateProp))
                    {
                        state = stateProp.GetString();
                    }

                    if (entity.TryGetProperty("attributes", out var attrs))
                    {
                        if (attrs.TryGetProperty("friendly_name", out var fname))
                        {
                            friendlyName = fname.GetString() ?? string.Empty;
                        }

                        if (attrs.TryGetProperty("device_class", out var deviceClassProp))
                        {
                            deviceClass = deviceClassProp.GetString();
                        }

                        if (attrs.TryGetProperty("unit_of_measurement", out var unitProp))
                        {
                            unitOfMeasurement = unitProp.GetString();
                        }

                        if (attrs.TryGetProperty("icon", out var iconProp))
                        {
                            icon = iconProp.GetString();
                        }

                        if (attrs.TryGetProperty("supported_features", out var supportedFeaturesProp))
                        {
                            if (supportedFeaturesProp.ValueKind == JsonValueKind.Number && supportedFeaturesProp.TryGetInt32(out var supported))
                            {
                                supportedFeatures = supported;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(entityId))
                    {
                        var domain = entityId.Split('.', 2)[0];
                        entities.Add(new HassEntity
                        {
                            EntityId = entityId,
                            FriendlyName = friendlyName,
                            Domain = domain,
                            DeviceClass = deviceClass,
                            UnitOfMeasurement = unitOfMeasurement,
                            Icon = icon,
                            State = state,
                            SupportedFeatures = supportedFeatures
                        });
                    }
                }
            }

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);

            return entities;
        }
        catch (OperationCanceledException) { throw; }
        catch (WebSocketException)
        {
            return "Unable to connect to Home Assistant WebSocket. Please check the Host URL and ensure it's accessible.";
        }
        catch (Exception ex)
        {
            return $"Failed to fetch entities: {ex.Message}";
        }
    }

    public async Task<Result<bool, string>> SendNotification(Dashboard dashboard, string message, string title = "EPaper Dashboard")
    {
        var validationResult = _connection.ValidateAndGetDashboard(dashboard.Id.ToString());
        if (validationResult.IsFailure)
        {
            return validationResult.Error;
        }

        var (hostUrl, token) = _connection.GetHostAndToken(dashboard);
        hostUrl = hostUrl.TrimEnd('/');

        try
        {
            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, _connection.WebSocketPath);
            
            var messageId = _connection.NextMessageId();
            await HomeAssistantConnectionService.SendMessageAsync(ws, new
            {
                id = messageId,
                type = "call_service",
                domain = "persistent_notification",
                service = "create",
                service_data = new
                {
                    title = title,
                    message = message,
                    notification_id = $"epaper_dashboard_{dashboard.Id}"
                }
            });

            var response = await HomeAssistantConnectionService.ReceiveMessageAsync(ws);
            var result = JsonSerializer.Deserialize<JsonElement>(response);

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);

            var isSuccess = result.TryGetProperty("success", out var success) && success.GetBoolean();
            if (!isSuccess)
            {
                var errorMsg = result.TryGetProperty("error", out var error) 
                    ? error.GetProperty("message").GetString() 
                    : "Unknown error";
                return $"Failed to send notification: {errorMsg}";
            }

            _logger.LogInformation("Notification sent to Home Assistant for dashboard {DashboardName}", dashboard.Name);
            return true;
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex, "Unable to connect to Home Assistant for dashboard {DashboardName}", dashboard.Name);
            return "Unable to connect to Home Assistant WebSocket";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification for dashboard {DashboardName}", dashboard.Name);
            return $"Failed to send notification: {ex.Message}";
        }
    }

    // =============================================
    // HA Dashboard / View discovery (UI only)
    // =============================================

    private async Task<List<HassUrlInfo>> FetchAllDashboardViews(ClientWebSocket ws, string hostUrl)
    {
        var results = new List<HassUrlInfo>();

        await HomeAssistantConnectionService.SendMessageAsync(ws, new
        {
            id = 1,
            type = "lovelace/dashboards/list"
        });

        var dashboardsResponse = await HomeAssistantConnectionService.ReceiveMessageAsync(ws);
        var dashboardsResult = JsonSerializer.Deserialize<JsonElement>(dashboardsResponse);

        var fetchedDashboards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var isSuccess = dashboardsResult.TryGetProperty("success", out var success) && success.GetBoolean();
        if (!isSuccess || !dashboardsResult.TryGetProperty("result", out var dashboardsArray))
        {
            await GetDashboardViews(ws, hostUrl, "lovelace", "Home", results);
            return results;
        }

        foreach (var hassDb in dashboardsArray.EnumerateArray())
        {
            var urlPath = hassDb.TryGetProperty("url_path", out var up) ? up.GetString() : null;
            var title = hassDb.TryGetProperty("title", out var t) ? t.GetString() : null;

            if (string.IsNullOrWhiteSpace(urlPath) || string.IsNullOrWhiteSpace(title))
                continue;

            await GetDashboardViews(ws, hostUrl, urlPath, title, results);
            fetchedDashboards.Add(urlPath);
        }

        if (!fetchedDashboards.Contains("lovelace"))
        {
            await GetDashboardViews(ws, hostUrl, "lovelace", "Home", results);
        }

        return results;
    }

    private async Task GetDashboardViews(ClientWebSocket ws, string hostUrl, string urlPath, string dashboardTitle, List<HassUrlInfo> results)
    {
        try
        {
            var messageId = _connection.NextMessageId();

            await HomeAssistantConnectionService.SendMessageAsync(ws, new
            {
                id = messageId,
                type = "lovelace/config",
                url_path = urlPath == "lovelace" ? (string?)null : urlPath
            });

            var configResponse = await HomeAssistantConnectionService.ReceiveMessageAsync(ws);
            var configResult = JsonSerializer.Deserialize<JsonElement>(configResponse);

            var isSuccess = configResult.TryGetProperty("success", out var success) && success.GetBoolean();
            if (!isSuccess)
            {
                results.AddRange(CreateDefaultDashboardInfo(hostUrl, urlPath, dashboardTitle));
                return;
            }

            if (!configResult.TryGetProperty("result", out var config) ||
                !config.TryGetProperty("views", out var views) ||
                views.ValueKind != JsonValueKind.Array)
            {
                results.AddRange(CreateDefaultDashboardInfo(hostUrl, urlPath, dashboardTitle));
                return;
            }

            var viewsArray = views.EnumerateArray().ToList();
            if (viewsArray.Count == 0)
            {
                results.AddRange(CreateDefaultDashboardInfo(hostUrl, urlPath, dashboardTitle));
                return;
            }

            results.AddRange(ConvertViewsToResults(viewsArray, hostUrl, urlPath, dashboardTitle));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching views for dashboard {Dashboard}", urlPath);
            results.AddRange(CreateDefaultDashboardInfo(hostUrl, urlPath, dashboardTitle));
        }
    }

    private static IEnumerable<HassUrlInfo> CreateDefaultDashboardInfo(string hostUrl, string urlPath, string dashboardTitle)
    {
        yield return new HassUrlInfo
        {
            Url = $"{hostUrl}/{urlPath}",
            Title = dashboardTitle,
            Id = urlPath
        };
    }

    private static IEnumerable<HassUrlInfo> ConvertViewsToResults(List<JsonElement> viewsArray, string hostUrl, string urlPath, string dashboardTitle)
    {
        for (int i = 0; i < viewsArray.Count; i++)
        {
            var view = viewsArray[i];
            var viewPath = view.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null;
            var viewTitle = view.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;

            var viewUrl = string.IsNullOrWhiteSpace(viewPath)
                ? $"{hostUrl}/{urlPath}/{i}"
                : $"{hostUrl}/{urlPath}/{viewPath}";

            var displayTitle = !string.IsNullOrWhiteSpace(viewTitle)
                ? $"{dashboardTitle} - {viewTitle}"
                : $"{dashboardTitle} - View {i + 1}";

            var viewId = string.IsNullOrWhiteSpace(viewPath)
                ? $"{urlPath}/{i}"
                : $"{urlPath}/{viewPath}";

            yield return new HassUrlInfo
            {
                Url = viewUrl,
                Title = displayTitle,
                Id = viewId
            };
        }
    }
}

public record HassUrlInfo
{
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
}

public record HassEntity
{
    public string EntityId { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string? DeviceClass { get; init; }
    public string? UnitOfMeasurement { get; init; }
    public string? Icon { get; init; }
    public string? State { get; init; }
    public int? SupportedFeatures { get; init; }
}
