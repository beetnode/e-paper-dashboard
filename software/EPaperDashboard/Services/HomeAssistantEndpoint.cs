namespace EPaperDashboard.Services;

/// <summary>A resolved Home Assistant server: its URL, a token, and the path of its WebSocket API.</summary>
public sealed record HomeAssistantEndpoint(string HostUrl, string Token, string WebSocketPath)
{
    public void Deconstruct(out string hostUrl, out string token)
    {
        hostUrl = HostUrl;
        token = Token;
    }
}
