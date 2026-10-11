namespace EPaperDashboard.RenderingComponent;

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
    int ProtocolVersion = 2)
{
    public const int MaximumDimension = 4096;
    public const int MaximumPixels = 16_000_000;
    public const int MaximumHtmlCharacters = 1_000_000;
    public const int MaximumImageBytes = 64_000_000;

    public void Validate()
    {
        if (ProtocolVersion != 2)
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
