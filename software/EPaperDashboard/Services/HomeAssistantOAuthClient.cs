namespace EPaperDashboard.Services;

/// <summary>
/// How izBoard identifies itself to Home Assistant's sign-in: the browser-facing app URL that receives the
/// callback, and the client ID, whose origin Home Assistant accepts redirect URIs on.
/// </summary>
public sealed record HomeAssistantOAuthClient(string AppUrl, string ClientId);
