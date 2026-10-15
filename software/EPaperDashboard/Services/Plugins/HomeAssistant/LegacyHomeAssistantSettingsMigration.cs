using System.Text.Json;
using System.Text.Json.Nodes;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
namespace EPaperDashboard.Services.Plugins.HomeAssistant;

/// <summary>
/// Moves the retired dashboard-level Home Assistant host/token settings into named connections at startup.
/// Dashboards sharing a server and token share one connection. Without a default connection, bare source IDs
/// referred to these settings, so the dashboard's widgets and conversation agent are switched to the connection too.
/// In the add-on, bare source IDs keep using its default connection and only Home Assistant dashboard rendering
/// needs the token. The settings are always cleared, including incomplete ones that cannot be migrated.
/// </summary>
public sealed class LegacyHomeAssistantSettingsMigration(
    IDashboardRepository dashboards,
    ProviderConnectionStore connections,
    IUnitOfWork unitOfWork,
    IDeploymentStrategy deployment,
    ILogger<LegacyHomeAssistantSettingsMigration> logger)
{
    // Rendering loads the Home Assistant web UI, which the add-on's supervisor proxy does not serve.
    internal const string AddonHomeAssistantUrl = "http://homeassistant:8123";

    private static bool HasLegacySettings(Dashboard dashboard) =>
        !string.IsNullOrWhiteSpace(dashboard.Host) || !string.IsNullOrWhiteSpace(dashboard.AccessToken);

    public void Run()
    {
        List<DashboardId> pending;
        try
        {
            pending = dashboards.GetAll().Where(HasLegacySettings).Select(d => d.Id).ToList();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not look for dashboards with retired Home Assistant settings");
            return;
        }

        foreach (var id in pending)
        {
            try
            {
                // Instances starting together take turns, so each sees connections the others created.
                unitOfWork.ExecuteExclusive(nameof(LegacyHomeAssistantSettingsMigration), () => Migrate(id));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not move the Home Assistant settings of dashboard {DashboardId} to a connection", id);
            }
        }
    }

    private bool Migrate(DashboardId id)
    {
        // Read again under the lock: another instance may have migrated it already.
        var found = dashboards.FindById(id);
        if (found is null || !HasLegacySettings(found)) return false;
        var dashboard = found;

        var hasDefaultConnection = connections.DefaultConnection(dashboard.UserId) is not null;
        var rendersWithSettings = dashboard.RenderingMode == RenderingMode.HomeAssistant && string.IsNullOrEmpty(dashboard.HomeAssistantConnectionId);
        if (!hasDefaultConnection || rendersWithSettings)
        {
            var host = ServerUrl(dashboard);
            var token = dashboard.AccessToken?.Trim();
            if (host is null || string.IsNullOrEmpty(token))
            {
                logger.LogWarning("Dashboard {DashboardId} had incomplete Home Assistant settings, which were removed. Select a connection for it.", dashboard.Id);
            }
            else
            {
                var connection = FindOrCreateConnection(dashboard.UserId, host, token);
                if (rendersWithSettings) dashboard.HomeAssistantConnectionId = connection.Id;
                if (!hasDefaultConnection)
                {
                    QualifySources(dashboard.LayoutConfig?.Widgets, connection.Id);
                    QualifySources(dashboard.AiGeneratedWidgets, connection.Id);
                    if (dashboard.AiConfig is { ConnectionMode: AiConnectionMode.HomeAssistant } ai && string.IsNullOrEmpty(ai.HomeAssistantConnectionId))
                        ai.HomeAssistantConnectionId = connection.Id;
                }
            }
        }

        dashboard.Host = null;
        dashboard.AccessToken = null;
        dashboards.Update(dashboard);
        return true;
    }

    private string? ServerUrl(Dashboard dashboard)
    {
        var host = deployment.DefaultHomeAssistant is not null && string.IsNullOrWhiteSpace(dashboard.Host)
            ? AddonHomeAssistantUrl
            : dashboard.Host?.Trim();
        return Uri.TryCreate(host, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)
            ? host!.TrimEnd('/')
            : null;
    }

    private ProviderConnection FindOrCreateConnection(UserId owner, string host, string token)
    {
        var existing = connections.List(owner).FirstOrDefault(connection =>
            connection.PluginId == HomeAssistantDashboardTargetResolver.PluginId && !connections.IsDefault(connection)
            && HasServer(connection, host, token));
        if (existing is not null) return existing;

        var created = new ProviderConnection
        {
            UserId = owner,
            PluginId = HomeAssistantDashboardTargetResolver.PluginId,
            Name = $"Home Assistant ({new Uri(host).Authority})"
        };
        connections.Create(created, new() { ["host"] = host, ["token"] = token });
        logger.LogInformation("Created Home Assistant connection {ConnectionId} from retired dashboard settings", created.Id);
        return created;
    }

    private bool HasServer(ProviderConnection connection, string host, string token)
    {
        try
        {
            var credentials = connections.Credentials(connection);
            return credentials.GetValueOrDefault("host")?.TrimEnd('/') == host && credentials.GetValueOrDefault("token") == token;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Credentials protected with a lost key cannot match.
            return false;
        }
    }

    private static void QualifySources(List<WidgetConfig>? widgets, string connectionId)
    {
        foreach (var widget in widgets ?? [])
        {
            if (widget.Config.ValueKind != JsonValueKind.Object) continue;
            var config = JsonNode.Parse(widget.Config.GetRawText())!;
            if (Qualify(config, connectionId)) widget.Config = JsonSerializer.SerializeToElement(config);
        }
    }

    // Widgets store source IDs as "entityId", including inside header badges and graph series.
    private static bool Qualify(JsonNode node, string connectionId)
    {
        var changed = false;
        if (node is JsonObject properties)
        {
            foreach (var (key, value) in properties.ToList())
            {
                if (key == "entityId" && value is JsonValue text && text.TryGetValue<string>(out var sourceId)
                    && !string.IsNullOrWhiteSpace(sourceId) && !SourceReference.IsQualified(sourceId))
                {
                    properties[key] = SourceReference.Create(connectionId, sourceId);
                    changed = true;
                }
                else if (value is not null)
                {
                    changed |= Qualify(value, connectionId);
                }
            }
        }
        else if (node is JsonArray items)
        {
            foreach (var item in items)
                if (item is not null) changed |= Qualify(item, connectionId);
        }
        return changed;
    }
}
