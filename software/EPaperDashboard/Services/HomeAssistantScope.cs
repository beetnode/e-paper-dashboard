using EPaperDashboard.Models;

namespace EPaperDashboard.Services;

/// <summary>Which Home Assistant server a request uses.</summary>
public abstract record HomeAssistantScope
{
    private HomeAssistantScope() { }

    /// <summary>The dashboard's retired host/token settings, or the add-on's automatic connection.</summary>
    public sealed record DashboardSettings(string DashboardId) : HomeAssistantScope;

    /// <summary>A named connection. Callers must have checked that it belongs to the requesting user.</summary>
    public sealed record Connection(ProviderConnection Value) : HomeAssistantScope;
}

/// <summary>A resolved Home Assistant server; named connections use the server's standard WebSocket path.</summary>
public sealed record HomeAssistantEndpoint(string HostUrl, string Token, string WebSocketPath)
{
    public void Deconstruct(out string hostUrl, out string token)
    {
        hostUrl = HostUrl;
        token = Token;
    }
}
