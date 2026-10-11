using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Plugins;

namespace EPaperDashboard.Services;

/// <summary>
/// Provides shared Home Assistant connection infrastructure: dashboard validation,
/// host/token resolution, WebSocket connectivity, and JSON helpers.
/// Used by HA provider implementations and HomeAssistantService.
/// </summary>
public class HomeAssistantConnectionService(
    DashboardService dashboardService,
    IDeploymentStrategy deploymentStrategy,
    ProviderConnectionStore? connections = null)
{
    private readonly DashboardService _dashboardService = dashboardService;
    private readonly IDeploymentStrategy _deploymentStrategy = deploymentStrategy;
    private int _messageId = 2;

    public string WebSocketPath => _deploymentStrategy.WebSocketPath;

    private const string ConnectionWebSocketPath = "/api/websocket";

    public (string host, string token) GetHostAndToken(Dashboard dashboard)
    {
        return _deploymentStrategy.GetHomeAssistantConnection(dashboard);
    }

    public Result<Dashboard, string> ValidateAndGetDashboard(string dashboardId)
    {
        if (string.IsNullOrWhiteSpace(dashboardId))
        {
            return "Dashboard ID is required";
        }

        if (!DashboardId.TryParse(dashboardId, out var dashboardIdTyped))
        {
            return "Invalid dashboard ID format";
        }

        var dashboardMaybe = _dashboardService.GetDashboardById(dashboardIdTyped);
        if (dashboardMaybe.HasNoValue)
        {
            return "Dashboard not found";
        }

        var dashboard = dashboardMaybe.Value;

        if (_deploymentStrategy.IsAutoConnected)
        {
            return dashboard;
        }

        return Result.Success<Dashboard, string>(dashboard)
            .Ensure(d => !string.IsNullOrWhiteSpace(d.Host), "Dashboard host is not configured")
            .Ensure(d => !string.IsNullOrWhiteSpace(d.AccessToken), "Dashboard access token is not set. Please authenticate with Home Assistant first.");
    }

    /// <summary>
    /// Returns a connected and authenticated WebSocket. Caller is responsible for disposing it.
    /// </summary>
    public async Task<ClientWebSocket> ConnectAsync(HomeAssistantScope scope, CancellationToken cancellationToken = default)
    {
        var endpoint = GetConnectionInfo(scope);
        if (endpoint.IsFailure)
        {
            throw new InvalidOperationException(endpoint.Error);
        }

        return await WebSocketHelpers.ConnectAndAuthenticateAsync(endpoint.Value.HostUrl, endpoint.Value.Token, endpoint.Value.WebSocketPath, cancellationToken);
    }

    /// <summary>Finds a named Home Assistant connection only if it belongs to <paramref name="owner"/>.</summary>
    public ProviderConnection? FindConnection(string connectionId, UserId owner) =>
        connections?.Find(connectionId, owner) is { PluginId: "home-assistant" } connection ? connection : null;

    /// <summary>
    /// Scope for a dashboard feature that may name a connection: the connection must belong to the dashboard owner.
    /// Without one, the dashboard's retired settings or the add-on's automatic connection are used.
    /// </summary>
    public Result<HomeAssistantScope, string> ResolveScope(string dashboardId, string? connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return new HomeAssistantScope.DashboardSettings(dashboardId);
        if (!DashboardId.TryParse(dashboardId, out var id)) return "Invalid dashboard ID format";
        var dashboard = _dashboardService.GetDashboardById(id);
        if (dashboard.HasNoValue) return "Dashboard not found";
        var connection = FindConnection(connectionId, dashboard.Value.UserId);
        return connection is null
            ? "The selected Home Assistant connection is unavailable."
            : new HomeAssistantScope.Connection(connection);
    }

    /// <summary>Resolves the server for a scope, after validating the dashboard settings when they are used.</summary>
    public Result<HomeAssistantEndpoint, string> GetConnectionInfo(HomeAssistantScope scope)
    {
        if (scope is HomeAssistantScope.Connection { Value: var connection })
        {
            if (connection.PluginId != "home-assistant" || connections is null) return "Connection not found";
            var credentials = connections.Credentials(connection);
            if (!credentials.TryGetValue("token", out var connectionToken) || string.IsNullOrWhiteSpace(connectionToken))
                return "Sign in to this Home Assistant connection from Connections first.";
            if (!credentials.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
                return "This Home Assistant connection has no server URL.";
            return new HomeAssistantEndpoint(host.TrimEnd('/'), connectionToken, ConnectionWebSocketPath);
        }

        var dashboardId = ((HomeAssistantScope.DashboardSettings)scope).DashboardId;
        var dashboardResult = ValidateAndGetDashboard(dashboardId);
        if (dashboardResult.IsFailure)
        {
            return dashboardResult.Error;
        }

        var (hostUrl, token) = GetHostAndToken(dashboardResult.Value);
        return new HomeAssistantEndpoint(hostUrl.TrimEnd('/'), token, WebSocketPath);
    }

    public int NextMessageId() => Interlocked.Increment(ref _messageId);

    public static async Task SendMessageAsync(ClientWebSocket ws, object message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    public static async Task<string> ReceiveMessageAsync(ClientWebSocket ws)
    {
        return await ReceiveMessageAsync(ws, CancellationToken.None);
    }

    public static async Task<string> ReceiveMessageAsync(ClientWebSocket ws, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 16];
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);
        return sb.ToString();
    }

    public static object? ExtractJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.TryGetDouble(out var d) ? d : null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(element.ToString()) ?? new Dictionary<string, object?>(),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(element.ToString()) ?? new List<object?>(),
            _ => null
        };
    }
}
