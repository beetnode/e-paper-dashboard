using EPaperDashboard.Models;
namespace EPaperDashboard.Services.Plugins.HomeAssistant;

/// <summary>
/// In the add-on, every user has a default Home Assistant connection to the add-on's own server through the
/// supervisor. It provides data only: rendering a Home Assistant dashboard needs a connection with a long-lived token.
/// </summary>
public sealed class HomeAssistantAddonConnection(IDeploymentStrategy deployment) : IDefaultConnectionSource
{
    public const string Id = "00000000000000000000000000add0e5";
    public const string Name = "Home Assistant add-on";

    public ProviderConnection? DefaultConnection(UserId owner) => deployment.DefaultHomeAssistant is not null
        ? new ProviderConnection { Id = Id, UserId = owner, PluginId = HomeAssistantDashboardTargetResolver.PluginId, Name = Name }
        : null;
}
