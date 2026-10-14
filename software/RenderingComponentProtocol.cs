namespace EPaperDashboard.RenderingComponent;

/// <summary>
/// The downloadable rendering component is versioned independently of the application. The application installs
/// the component release <c>rendering-v{Version}</c> and accepts any installed component speaking its protocol.
/// </summary>
internal static class RenderingComponentInfo
{
    /// <summary>Bump for every change to the component payload: worker code, Playwright, browser or native libraries.</summary>
    public const string Version = "1.0.0";

    /// <summary>Bump, together with <see cref="Version"/>, when the request or response contract changes incompatibly.</summary>
    public const int ProtocolVersion = 2;
}

internal sealed record RenderRequest(
    string Mode,
    int Width,
    int Height,
    string? OutputPath = null,
    string? DashboardUri = null,
    string? Html = null,
    string? AccessToken = null,
    string? TokenType = null,
    string? HassUrl = null,
    string? ClientId = null,
    int ProtocolVersion = RenderingComponentInfo.ProtocolVersion)
{
    public const int MaximumDimension = 4096;
    public const int MaximumPixels = 16_000_000;
    public const int MaximumHtmlCharacters = 1_000_000;
    public const int MaximumImageBytes = 64_000_000;

    public void Validate()
    {
        if (ProtocolVersion != RenderingComponentInfo.ProtocolVersion)
            throw new InvalidOperationException("Unsupported rendering protocol version.");
        if (Mode is not ("dashboard" or "html"))
            throw new InvalidOperationException("Unsupported render mode.");
        if (Width <= 0 || Height <= 0 || Width > MaximumDimension || Height > MaximumDimension
            || (long)Width * Height > MaximumPixels)
            throw new InvalidOperationException("The rendering viewport exceeds the supported limits.");
        if (Mode == "html" && (Html is null || Html.Length > MaximumHtmlCharacters))
            throw new InvalidOperationException("HTML content is missing or exceeds the supported limit.");
        if (Mode == "dashboard")
        {
            if (DashboardUri?.Length > 8192 || HassUrl?.Length > 8192 || ClientId?.Length > 8192
                || AccessToken?.Length > 16384 || TokenType?.Length > 128)
                throw new InvalidOperationException("Dashboard authorization details exceed the supported limits.");
            if (!Uri.TryCreate(DashboardUri, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Dashboard URLs must use HTTP or HTTPS.");
            if (string.IsNullOrWhiteSpace(AccessToken) || string.IsNullOrWhiteSpace(TokenType)
                || string.IsNullOrWhiteSpace(HassUrl) || string.IsNullOrWhiteSpace(ClientId))
                throw new InvalidOperationException("Dashboard authorization details are incomplete.");
        }
    }
}

internal sealed record RenderResponse(bool Success, string? Error, int ProtocolVersion = 0);

internal sealed record RemoteRenderRequest(string ComponentDirectory, string AppVersion, RenderRequest Request, Guid JobId);
internal sealed record RenderingHostHealth(string AppVersion, string RuntimeIdentifier, int ProtocolVersion, bool SandboxRequired, bool SupportsJobCancellation = false);
