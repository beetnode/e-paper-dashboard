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

    public (string Host, string Token) ConnectionServer(ProviderConnection connection)
    {
        var credentials = connections.Credentials(connection);
        return (credentials.GetValueOrDefault("host") ?? "", credentials.GetValueOrDefault("token") ?? "");
    }

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
                ? "The selected Home Assistant connection has no valid server URL."
                : "Dashboard configuration incomplete. Set the Home Assistant host.";
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
            return deployment.IsAutoConnected && !UsesConnection(dashboard)
                ? "Dashboard configuration incomplete. The 'Home Assistant Dashboard' rendering mode requires a Long-Lived Access Token. " +
                  "Create one in Home Assistant (Profile → Long-Lived Access Tokens) and set it on the dashboard, or select a Home Assistant connection."
                : "Dashboard configuration incomplete. A Home Assistant access token is required.";

        var hassUrl = new Uri(host).AbsoluteUri.TrimEnd('/');
        // Long-lived tokens do not use the client ID; prefer the configured client URI as a stable value.
        var clientId = environment.ClientUri?.AbsoluteUri.TrimEnd('/') ?? hassUrl;
        return new HomeAssistantDashboardTarget(uri.Value, new HassTokens(token, "Bearer", hassUrl, clientId));
    }
}
