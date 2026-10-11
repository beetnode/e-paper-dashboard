using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.WebSockets;
using CSharpFunctionalExtensions;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using static EPaperDashboard.Utilities.DeploymentMode;

namespace EPaperDashboard.Services;

public class HomeAssistantAuthService(
    ILogger<HomeAssistantAuthService> logger,
    IHttpClientFactory httpClientFactory,
    IDeploymentStrategy deploymentStrategy,
    TimeProvider timeProvider)
{
    private readonly ILogger<HomeAssistantAuthService> _logger = logger;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IDeploymentStrategy _deploymentStrategy = deploymentStrategy;
    private readonly TimeProvider _timeProvider = timeProvider;
    private static readonly Lazy<byte[]> _stateSigningKey = new(() => 
    {
        var key = EnvironmentConfiguration.StateSigningKey;
        return key != null ? Encoding.UTF8.GetBytes(key) : Array.Empty<byte>();
    });
    private static byte[] StateSigningKey => _stateSigningKey.Value;
    
    private readonly ConcurrentDictionary<string, bool> _activeAuthFlows = new();
    private readonly ConcurrentDictionary<string, DateTime> _authFlowTimestamps = new();

    public AuthStartResult StartAuth(string host, string dashboardId, HttpContext? httpContext = null, string? internalHost = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return AuthStartResult.Failure("Host is required");
        }

        if (string.IsNullOrWhiteSpace(dashboardId))
        {
            return AuthStartResult.Failure("DashboardId is required");
        }

        if (_activeAuthFlows.ContainsKey(dashboardId))
        {
            _logger.LogInformation("Cancelling existing auth flow for dashboard {DashboardId} and starting new one", dashboardId);
        }

        _activeAuthFlows[dashboardId] = true;
        _authFlowTimestamps[dashboardId] = _timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            var clientUri = _deploymentStrategy.GetOAuthClientUri(httpContext);
            if (clientUri == null)
            {
                _activeAuthFlows.TryRemove(dashboardId, out _);
                _authFlowTimestamps.TryRemove(dashboardId, out _);
                var errorMsg = _deploymentStrategy.Mode == DeploymentMode.Addon
                    ? "Could not determine ingress URL from request. Ensure you're accessing the addon through Home Assistant ingress."
                    : "CLIENT_URL is not configured. This is required for Home Assistant OAuth authentication.";
                return AuthStartResult.Failure(errorMsg);
            }

            var hostUri = new Uri(host);
            var hostUrl = hostUri.ToString().TrimEnd('/');

            string clientId;
            string redirectUri;
            if (_deploymentStrategy.Mode == DeploymentMode.Addon)
            {
                clientId = hostUrl;
                redirectUri = $"{clientUri.ToString().TrimEnd('/')}/api/homeassistant/callback";
            }
            else
            {
                clientId = clientUri.ToString().TrimEnd('/');
                redirectUri = $"{clientId}/api/homeassistant/callback";
            }

            var stateData = new StateData
            {
                DashboardId = dashboardId,
                Host = internalHost?.TrimEnd('/') ?? hostUrl,
                ClientId = clientId,
                Timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds()
            };
            var state = EncodeState(stateData);

            var authUrl = $"{hostUrl}/auth/authorize?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";

            _logger.LogInformation("Starting auth flow for {Host} with dashboard {DashboardId}", hostUrl, dashboardId);

            return AuthStartResult.Success(authUrl, state);
        }
        catch (Exception ex)
        {
            _activeAuthFlows.TryRemove(dashboardId, out _);
            _authFlowTimestamps.TryRemove(dashboardId, out _);
            _logger.LogError(ex, "Error starting Home Assistant authentication");
            return AuthStartResult.Failure($"Failed to start authentication: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts Home Assistant sign-in for a named connection. The callback stores a long-lived token in the
    /// connection itself, so no token is passed through the browser.
    /// </summary>
    public Result<(string Url, string State), string> StartConnectionAuth(ProviderConnection connection, string host, HttpContext? httpContext)
    {
        if (!Uri.TryCreate(host, UriKind.Absolute, out var hostUri))
            return "Enter the Home Assistant server URL first.";
        var clientUri = _deploymentStrategy.GetOAuthClientUri(httpContext);
        if (clientUri == null)
            return _deploymentStrategy.Mode == DeploymentMode.Addon
                ? "Could not determine ingress URL from request. Ensure you're accessing the addon through Home Assistant ingress."
                : "CLIENT_URL is not configured. This is required for Home Assistant sign-in.";

        var hostUrl = hostUri.ToString().TrimEnd('/');
        var appUrl = clientUri.ToString().TrimEnd('/');
        // Home Assistant accepts redirect URIs on the client ID's origin. Under ingress that origin is Home Assistant's.
        var clientId = _deploymentStrategy.Mode == DeploymentMode.Addon ? clientUri.GetLeftPart(UriPartial.Authority) : appUrl;
        var state = EncodeState(new StateData
        {
            Host = hostUrl,
            ClientId = clientId,
            ConnectionId = connection.Id,
            OwnerId = connection.UserId.Value,
            Timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds()
        });
        var redirectUri = $"{appUrl}/api/providers/home-assistant/callback";
        var authUrl = $"{hostUrl}/auth/authorize?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";
        return (authUrl, state);
    }

    /// <summary>Exchanges the authorization code and creates a long-lived token for the connection named in the signed state.</summary>
    public async Task<Result<ConnectionAuthorization, string>> CompleteConnectionAuthAsync(string? code, string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return "Home Assistant sign-in was cancelled or expired.";
        var stateData = DecodeState(state);
        if (stateData?.ConnectionId is null || stateData.OwnerId is null || stateData.ClientId is null)
            return "Home Assistant sign-in expired. Start again from Connections.";
        var age = _timeProvider.GetUtcNow().ToUnixTimeSeconds() - stateData.Timestamp;
        if (age is > 300 or < 0)
            return "Home Assistant sign-in expired. Start again from Connections.";

        try
        {
            var response = await _httpClientFactory.CreateClient().PostAsync($"{stateData.Host}/auth/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "client_id", stateData.ClientId }
            }), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Home Assistant token exchange failed: {StatusCode}", response.StatusCode);
                return "Home Assistant did not accept the sign-in. Try again.";
            }
            var token = JsonSerializer.Deserialize<TokenResponse>(await response.Content.ReadAsStringAsync(cancellationToken));
            if (token?.AccessToken == null)
                return "Home Assistant returned an invalid token response.";
            var longLived = await CreateLongLivedTokenViaWebSocket(stateData.Host, token.AccessToken);
            return new ConnectionAuthorization(stateData.ConnectionId, new UserId(stateData.OwnerId), longLived);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Do not return exception text: it can contain server responses.
            _logger.LogWarning("Home Assistant sign-in failed ({ExceptionType})", ex.GetType().Name);
            return "Unable to complete Home Assistant sign-in. Ensure the server URL is reachable from izBoard.";
        }
    }

    public async Task<AuthCallbackResult> HandleCallback(string? code, string? state, string? error, HttpContext? httpContext = null)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.LogWarning("Home Assistant authorization failed: {Error}", error);
            return AuthCallbackResult.Failure(null, error);
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return AuthCallbackResult.Failure(null, "Missing code or state parameter");
        }

        var stateData = DecodeState(state);
        if (stateData == null || stateData.ConnectionId != null)
        {
            // Connection sign-in states complete only at the provider callback, which stores the token server-side.
            return AuthCallbackResult.Failure(null, "Invalid or tampered state parameter");
        }

        var age = _timeProvider.GetUtcNow().ToUnixTimeSeconds() - stateData.Timestamp;
        if (age > 300)
        {
            return AuthCallbackResult.Failure(stateData.DashboardId, "Authentication request expired");
        }
        
        if (age < 0)
        {
            return AuthCallbackResult.Failure(stateData.DashboardId, "Invalid state timestamp");
        }

        try
        {
            string clientId;
            if (!string.IsNullOrWhiteSpace(stateData.ClientId))
            {
                clientId = stateData.ClientId;
            }
            else
            {
                var clientUri = _deploymentStrategy.GetOAuthClientUri(httpContext);
                if (clientUri == null)
                {
                    var errorMsg = _deploymentStrategy.Mode == DeploymentMode.Addon
                        ? "Could not determine ingress URL for OAuth callback."
                        : "CLIENT_URL is not configured.";
                    return AuthCallbackResult.Failure(stateData.DashboardId, errorMsg);
                }
                clientId = clientUri.ToString().TrimEnd('/');
            }

            var tokenUrl = $"{stateData.Host}/auth/token";

            var httpClient = _httpClientFactory.CreateClient();
            var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "client_id", clientId }
            });

            _logger.LogInformation("Exchanging code for token at {TokenUrl}", tokenUrl);
            var response = await httpClient.PostAsync(tokenUrl, requestBody);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange failed: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return AuthCallbackResult.Failure(stateData.DashboardId, $"Token exchange failed: {response.StatusCode}");
            }

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
            if (tokenResponse?.AccessToken == null)
            {
                return AuthCallbackResult.Failure(stateData.DashboardId, "Invalid token response");
            }

            var longLived = await CreateLongLivedTokenViaWebSocket(stateData.Host, tokenResponse.AccessToken);
            _logger.LogInformation("Created long-lived token for dashboard {DashboardId}", stateData.DashboardId);
            return AuthCallbackResult.Success(stateData.DashboardId, longLived);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during token exchange");
            var errorMsg = "Unable to connect to Home Assistant. Please ensure the Home Assistant instance is accessible from this server";
            if (ex.InnerException is System.Net.Sockets.SocketException)
            {
                errorMsg += " and check that the Host URL is correct.";
            }
            return AuthCallbackResult.Failure(stateData.DashboardId, errorMsg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token exchange");
            return AuthCallbackResult.Failure(stateData.DashboardId, $"Unexpected error: {ex.Message}");
        }
        finally
        {
            _activeAuthFlows.TryRemove(stateData.DashboardId, out _);
            _authFlowTimestamps.TryRemove(stateData.DashboardId, out _);
        }
    }

    private static string EncodeState(StateData data)
    {
        var json = JsonSerializer.Serialize(data);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        
        using var hmac = new HMACSHA256(StateSigningKey);
        var signature = hmac.ComputeHash(jsonBytes);
        
        var combined = new byte[jsonBytes.Length + signature.Length];
        Buffer.BlockCopy(jsonBytes, 0, combined, 0, jsonBytes.Length);
        Buffer.BlockCopy(signature, 0, combined, jsonBytes.Length, signature.Length);
        
        return Convert.ToBase64String(combined)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static StateData? DecodeState(string state)
    {
        try
        {
            var base64 = state.Replace('-', '+').Replace('_', '/');
            switch (state.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            
            var combined = Convert.FromBase64String(base64);
            
            if (combined.Length < 33)
                return null;
                
            var signatureLength = 32;
            var jsonBytes = new byte[combined.Length - signatureLength];
            var signature = new byte[signatureLength];
            
            Buffer.BlockCopy(combined, 0, jsonBytes, 0, jsonBytes.Length);
            Buffer.BlockCopy(combined, jsonBytes.Length, signature, 0, signatureLength);
            
            using var hmac = new HMACSHA256(StateSigningKey);
            var expectedSignature = hmac.ComputeHash(jsonBytes);
            
            if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
                return null;
            
            var json = Encoding.UTF8.GetString(jsonBytes);
            return JsonSerializer.Deserialize<StateData>(json);
        }
        catch
        {
            return null;
        }
    }

    private class StateData
    {
        public string DashboardId { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string? ClientId { get; set; }
        public string? ConnectionId { get; set; }
        public string? OwnerId { get; set; }
        public long Timestamp { get; set; }
    }

    private class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private async Task<string> CreateLongLivedTokenViaWebSocket(string host, string accessToken)
    {
        using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(host, accessToken);

        var messageId = 1;

        await WebSocketHelpers.SendMessageAsync(ws, new
        {
            id = messageId++,
            type = "auth/long_lived_access_token",
            client_name = $"EPaperDashboard-{Guid.NewGuid():N}",
            lifespan = 3650
        });

        var createResponse = await WebSocketHelpers.ReceiveMessageAsync(ws);
        var createResult = JsonSerializer.Deserialize<JsonElement>(createResponse);

        if (createResult.TryGetProperty("success", out var successProp) && successProp.GetBoolean()
            && createResult.TryGetProperty("result", out var resultProp)
            && resultProp.ValueKind == JsonValueKind.String)
        {
            var token = resultProp.GetString();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException($"Long-lived token creation returned empty token: {createResponse}");

            return token;
        }

        throw new InvalidOperationException($"Failed to create long-lived token: {createResponse}");
    }

    
}

public sealed record ConnectionAuthorization(string ConnectionId, UserId Owner, string Token);

public record AuthStartResult
{
    public bool IsSuccess { get; init; }
    public string? AuthUrl { get; init; }
    public string? State { get; init; }
    public string? Error { get; init; }

    public static AuthStartResult Success(string authUrl, string state) =>
        new() { IsSuccess = true, AuthUrl = authUrl, State = state };

    public static AuthStartResult Failure(string error) =>
        new() { IsSuccess = false, Error = error };
}

public record AuthCallbackResult
{
    public bool IsSuccess { get; init; }
    public string? DashboardId { get; init; }
    public string? AccessToken { get; init; }
    public string? Error { get; init; }

    public static AuthCallbackResult Success(string dashboardId, string accessToken) =>
        new() { IsSuccess = true, DashboardId = dashboardId, AccessToken = accessToken };

    public static AuthCallbackResult Failure(string? dashboardId, string error) =>
        new() { IsSuccess = false, DashboardId = dashboardId, Error = error };
}
