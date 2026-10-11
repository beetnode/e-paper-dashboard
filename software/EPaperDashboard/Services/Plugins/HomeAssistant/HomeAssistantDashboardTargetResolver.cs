using CSharpFunctionalExtensions;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
namespace EPaperDashboard.Services.Plugins.HomeAssistant;

public sealed record HomeAssistantDashboardTarget(Uri DashboardUri, HassTokens Tokens);

/// <summary>
/// Resolves the Home Assistant page rendered by "Home Assistant Dashboard" mode. A dashboard either references
/// a named Home Assistant connection owned by its user, or keeps its legacy host/token settings.
/// </summary>
public sealed class HomeAssistantDashboardTargetResolver(
    IDeploymentStrategy deployment,
    IEnvironmentConfiguration environment,
    ProviderConnectionStore connections)
{
    public const string PluginId = "home-assistant";

    public bool UsesConnection(Dashboard dashboard) => !string.IsNullOrEmpty(dashboard.HomeAssistantConnectionId);

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

    private string? ConnectionName(Dashboard dashboard) =>
        UsesConnection(dashboard) ? FindConnection(dashboard.HomeAssistantConnectionId!, dashboard.UserId)?.Name : null;

    public Result<(string Host, string Token), string> ResolveServer(Dashboard dashboard)
    {
        if (UsesConnection(dashboard))
        {
            var connection = FindConnection(dashboard.HomeAssistantConnectionId!, dashboard.UserId);
            if (connection is null) return "The selected Home Assistant connection is unavailable. Choose another connection.";
            return ConnectionServer(connection);
        }

        var host = dashboard.Host;
        if (string.IsNullOrWhiteSpace(host) && deployment.Mode != DeploymentMode.Standalone)
        {
            // Browser rendering needs the Home Assistant web UI. The add-on's supervisor proxy does not serve it.
            host = deployment.Mode == DeploymentMode.Addon
                ? Constants.HomeAssistantInternalUrl
                : deployment.GetHomeAssistantConnection(dashboard).host;
        }

        // Rendering always uses the dashboard's long-lived token, including in the add-on.
        return (host ?? "", dashboard.AccessToken ?? "");
    }

    public Result<Uri, string> ResolveUri(Dashboard dashboard)
    {
        var server = ResolveServer(dashboard);
        if (server.IsFailure) return server.Error;
        if (!Uri.TryCreate(server.Value.Host, UriKind.Absolute, out var hostUri) || hostUri.Scheme is not ("http" or "https"))
            return UsesConnection(dashboard)
                ? $"The Home Assistant connection '{ConnectionName(dashboard)}' has no valid server URL. Fix it on the Connections page."
                : "Dashboard configuration incomplete. The legacy Home Assistant host is missing; select a connection instead.";
        if (string.IsNullOrWhiteSpace(dashboard.Path) || !Uri.TryCreate(dashboard.Path, UriKind.Relative, out var pathUri))
            return "Dashboard configuration incomplete. Select the Home Assistant dashboard path.";
        return new Uri(hostUri, pathUri);
    }

    public Result<HomeAssistantDashboardTarget, string> Resolve(Dashboard dashboard)
    {
        var uri = ResolveUri(dashboard);
        if (uri.IsFailure) return uri.Error;
        var (host, token) = ResolveServer(dashboard).Value;
        if (string.IsNullOrWhiteSpace(token))
            return UsesConnection(dashboard)
                ? $"The Home Assistant connection '{ConnectionName(dashboard)}' is not signed in. Sign in or add a token on the Connections page."
                : "Dashboard configuration incomplete. Select a Home Assistant connection for this dashboard.";

        var hassUrl = new Uri(host).AbsoluteUri.TrimEnd('/');
        // Long-lived tokens do not use the client ID; prefer the configured client URI as a stable value.
        var clientId = environment.ClientUri?.AbsoluteUri.TrimEnd('/') ?? hassUrl;
        return new HomeAssistantDashboardTarget(uri.Value, new HassTokens(token, "Bearer", hassUrl, clientId));
    }
}
