using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
namespace EPaperDashboard.Services.Plugins.HomeAssistant;

public sealed record HomeAssistantDashboardTarget(Uri DashboardUri, HassTokens Tokens);

/// <summary>
/// Resolves the Home Assistant page rendered by "Home Assistant Dashboard" mode from the named Home Assistant
/// connection the dashboard references. The add-on's default connection cannot render: the browser needs a long-lived token.
/// </summary>
public sealed class HomeAssistantDashboardTargetResolver(
    IEnvironmentConfiguration environment,
    ProviderConnectionStore connections)
{
    public const string PluginId = "home-assistant";

    public const string DefaultConnectionCannotRender =
        "The Home Assistant add-on connection provides data only. To render a Home Assistant dashboard, " +
        "add a Home Assistant connection with a long-lived access token on the Connections page and select it.";

    private static bool UsesConnection(Dashboard dashboard) => !string.IsNullOrEmpty(dashboard.HomeAssistantConnectionId);

    /// <summary>Finds a named Home Assistant connection only if it belongs to <paramref name="owner"/>.</summary>
    public ProviderConnection? FindConnection(string connectionId, UserId owner)
    {
        var connection = connections.Find(connectionId, owner);
        return connection?.PluginId == PluginId ? connection : null;
    }

    private (string Host, string Token) ConnectionServer(ProviderConnection connection)
    {
        var credentials = connections.Credentials(connection);
        return (credentials.GetValueOrDefault("host") ?? "", credentials.GetValueOrDefault("token") ?? "");
    }

    private string? ConnectionName(Dashboard dashboard) => FindConnection(dashboard.HomeAssistantConnectionId!, dashboard.UserId)?.Name;

    /// <summary>Whether a connection can render Home Assistant dashboards; the add-on's default connection cannot.</summary>
    public bool CanRender(ProviderConnection connection) => !connections.IsDefault(connection);

    public Result<(string Host, string Token)> ResolveServer(Dashboard dashboard)
    {
        if (!UsesConnection(dashboard))
            return "Dashboard configuration incomplete. Select a Home Assistant connection with a long-lived access token for this dashboard.";
        var connection = FindConnection(dashboard.HomeAssistantConnectionId!, dashboard.UserId);
        if (connection is null) return "The selected Home Assistant connection is unavailable. Choose another connection.";
        if (!CanRender(connection)) return DefaultConnectionCannotRender;
        return ConnectionServer(connection);
    }

    public Result<Uri> ResolveUri(Dashboard dashboard)
    {
        var server = ResolveServer(dashboard);
        if (server.IsFailure) return server.Error;
        if (!Uri.TryCreate(server.Value.Host, UriKind.Absolute, out var hostUri) || hostUri.Scheme is not ("http" or "https"))
            return $"The Home Assistant connection '{ConnectionName(dashboard)}' has no valid server URL. Fix it on the Connections page.";
        if (string.IsNullOrWhiteSpace(dashboard.Path) || !Uri.TryCreate(dashboard.Path, UriKind.Relative, out var pathUri))
            return "Dashboard configuration incomplete. Select the Home Assistant dashboard path.";
        return new Uri(hostUri, pathUri);
    }

    public Result<HomeAssistantDashboardTarget> Resolve(Dashboard dashboard)
    {
        var uri = ResolveUri(dashboard);
        if (uri.IsFailure) return uri.Error;
        var (host, token) = ResolveServer(dashboard).Value;
        if (string.IsNullOrWhiteSpace(token))
            return $"The Home Assistant connection '{ConnectionName(dashboard)}' is not signed in. Sign in or add a token on the Connections page.";

        var hassUrl = new Uri(host).AbsoluteUri.TrimEnd('/');
        // Long-lived tokens do not use the client ID; prefer the configured client URI as a stable value.
        var clientId = environment.ClientUri?.AbsoluteUri.TrimEnd('/') ?? hassUrl;
        return new HomeAssistantDashboardTarget(uri.Value, new HassTokens(token, "Bearer", hassUrl, clientId));
    }
}
