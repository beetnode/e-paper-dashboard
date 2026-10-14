using System.Security.Cryptography;
using System.Text.Json;
using CSharpFunctionalExtensions;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using Microsoft.AspNetCore.DataProtection;

namespace EPaperDashboard.Services;

public sealed record ConnectionAuthorization(string ConnectionId, UserId Owner, string Token);

/// <summary>
/// Home Assistant sign-in for named connections. The state is protected with ASP.NET Core Data Protection,
/// so it needs no pending in-memory flow and survives restarts within its five-minute lifetime. The callback
/// stores a long-lived token in the connection; no token passes through the browser.
/// </summary>
public class HomeAssistantAuthService(
    ILogger<HomeAssistantAuthService> logger,
    IHttpClientFactory httpClientFactory,
    IDeploymentStrategy deploymentStrategy,
    TimeProvider timeProvider,
    IDataProtectionProvider dataProtection)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(5);
    private readonly IDataProtector _stateProtector = dataProtection.CreateProtector("izboard.home-assistant.sign-in.v1");

    public Result<(string Url, string State), string> StartConnectionAuth(ProviderConnection connection, string host, HttpContext? httpContext)
    {
        if (!Uri.TryCreate(host, UriKind.Absolute, out var hostUri))
            return "Enter the Home Assistant server URL first.";
        var client = deploymentStrategy.GetHomeAssistantOAuthClient(httpContext);
        if (client.IsFailure)
            return client.Error;

        var hostUrl = hostUri.ToString().TrimEnd('/');
        var (appUrl, clientId) = client.Value;
        var state = _stateProtector.Protect(JsonSerializer.Serialize(new StateData(
            hostUrl, clientId, connection.Id, connection.UserId.Value, timeProvider.GetUtcNow().ToUnixTimeSeconds())));
        var redirectUri = $"{appUrl}/api/providers/home-assistant/callback";
        var authUrl = $"{hostUrl}/auth/authorize?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";
        return (authUrl, state);
    }

    /// <summary>Exchanges the authorization code and creates a long-lived token for the connection named in the protected state.</summary>
    public async Task<Result<ConnectionAuthorization, string>> CompleteConnectionAuthAsync(string? code, string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return "Home Assistant sign-in was cancelled or expired.";
        var stateData = DecodeState(state);
        var age = stateData is null ? -1 : timeProvider.GetUtcNow().ToUnixTimeSeconds() - stateData.Timestamp;
        if (stateData is null || age < 0 || age > StateLifetime.TotalSeconds)
            return "Home Assistant sign-in expired. Start again from Connections.";

        try
        {
            var response = await httpClientFactory.CreateClient().PostAsync($"{stateData.Host}/auth/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "client_id", stateData.ClientId }
            }), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Home Assistant token exchange failed: {StatusCode}", response.StatusCode);
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
            logger.LogWarning("Home Assistant sign-in failed ({ExceptionType})", ex.GetType().Name);
            return "Unable to complete Home Assistant sign-in. Ensure the server URL is reachable from izBoard.";
        }
    }

    private StateData? DecodeState(string state)
    {
        try { return JsonSerializer.Deserialize<StateData>(_stateProtector.Unprotect(state)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { return null; }
    }

    private sealed record StateData(string Host, string ClientId, string ConnectionId, string OwnerId, long Timestamp);

    private sealed class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private static async Task<string> CreateLongLivedTokenViaWebSocket(string host, string accessToken)
    {
        using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(host, accessToken);

        await WebSocketHelpers.SendMessageAsync(ws, new
        {
            id = 1,
            type = "auth/long_lived_access_token",
            client_name = $"EPaperDashboard-{Guid.NewGuid():N}",
            lifespan = 3650
        });

        var createResponse = await WebSocketHelpers.ReceiveMessageAsync(ws);
        var createResult = JsonSerializer.Deserialize<JsonElement>(createResponse);

        if (createResult.TryGetProperty("success", out var successProp) && successProp.GetBoolean()
            && createResult.TryGetProperty("result", out var resultProp)
            && resultProp.ValueKind == JsonValueKind.String
            && resultProp.GetString() is { Length: > 0 } token)
        {
            return token;
        }

        // The response can echo request details; callers log only the exception type.
        throw new InvalidOperationException("Failed to create a long-lived Home Assistant token.");
    }
}
