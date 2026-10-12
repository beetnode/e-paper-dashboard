using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Plugins;

namespace EPaperDashboard.Services;

/// <summary>
/// Provides shared Home Assistant connection infrastructure: connection lookup, server and token resolution,
/// WebSocket connectivity, and JSON helpers. Used by HA provider implementations and HomeAssistantService.
/// </summary>
public class HomeAssistantConnectionService(
    DashboardService dashboardService,
    IDeploymentStrategy deploymentStrategy,
    ProviderConnectionStore connections)
{
    private readonly DashboardService _dashboardService = dashboardService;
    private readonly IDeploymentStrategy _deploymentStrategy = deploymentStrategy;
    private int _messageId = 2;

    private const string WebSocketPath = "/api/websocket";

    /// <summary>
    /// Returns a connected and authenticated WebSocket. Caller is responsible for disposing it.
    /// </summary>
    public async Task<ClientWebSocket> ConnectAsync(ProviderConnection homeAssistant, CancellationToken cancellationToken = default)
    {
        var endpoint = GetConnectionInfo(homeAssistant);
        if (endpoint.IsFailure)
        {
            throw new InvalidOperationException(endpoint.Error);
        }

        return await WebSocketHelpers.ConnectAndAuthenticateAsync(endpoint.Value.HostUrl, endpoint.Value.Token, endpoint.Value.WebSocketPath, cancellationToken);
    }

    /// <summary>Finds a named Home Assistant connection only if it belongs to <paramref name="owner"/>.</summary>
    public ProviderConnection? FindConnection(string connectionId, UserId owner) =>
        connections.Find(connectionId, owner) is { PluginId: "home-assistant" } connection ? connection : null;

    /// <summary>
    /// The connection a dashboard feature uses: the named one, which must belong to the dashboard owner,
    /// or else the owner's default connection (the add-on's own server).
    /// </summary>
    public Result<ProviderConnection, string> ResolveConnection(Dashboard dashboard, string? connectionId)
    {
        var connection = string.IsNullOrEmpty(connectionId)
            ? connections.DefaultConnection(dashboard.UserId)
            : FindConnection(connectionId, dashboard.UserId);
        if (connection is not null) return connection;
        return string.IsNullOrEmpty(connectionId)
            ? "Select a Home Assistant connection."
            : "The selected Home Assistant connection is unavailable.";
    }

    /// <inheritdoc cref="ResolveConnection(Dashboard, string?)"/>
    public Result<ProviderConnection, string> ResolveConnection(string dashboardId, string? connectionId)
    {
        if (!DashboardId.TryParse(dashboardId, out var id)) return "Invalid dashboard ID format";
        var dashboard = _dashboardService.GetDashboardById(id);
        return dashboard.HasNoValue ? "Dashboard not found" : ResolveConnection(dashboard.Value, connectionId);
    }

    /// <summary>Resolves the server and token of a connection. Callers must have checked that it belongs to the requesting user.</summary>
    public Result<HomeAssistantEndpoint, string> GetConnectionInfo(ProviderConnection homeAssistant)
    {
        if (homeAssistant.PluginId != "home-assistant") return "Connection not found";
        if (connections.IsDefault(homeAssistant))
        {
            return _deploymentStrategy.DefaultHomeAssistant is { } endpoint
                ? endpoint with { HostUrl = endpoint.HostUrl.TrimEnd('/') }
                : "Connection not found";
        }

        var credentials = connections.Credentials(homeAssistant);
        if (!credentials.TryGetValue("token", out var token) || string.IsNullOrWhiteSpace(token))
            return "Sign in to this Home Assistant connection from Connections first.";
        if (!credentials.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
            return "This Home Assistant connection has no server URL.";
        return new HomeAssistantEndpoint(host.TrimEnd('/'), token, WebSocketPath);
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
