using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using EPaperDashboard.Utilities;
using EPaperDashboard.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace EPaperDashboard.Services.Plugins.Google;

/// <summary>
/// Per-connection OAuth clients, browser-bound state, and encrypted refresh tokens. The state is protected with
/// Data Protection instead of being held in memory, so sign-in survives restarts and works across replicas.
/// It is effectively single-use: completing sign-in changes the connection's credentials, which the state pins.
/// </summary>
public sealed class GoogleOAuthService(
    ProviderConnectionStore connections, IHttpClientFactory clients, IMemoryCache cache, IDataProtectionProvider protection, TimeProvider clock)
{
    public const string RefreshTokenKey = "refreshToken";
    private const string Scopes = "https://www.googleapis.com/auth/calendar.calendarlist.readonly https://www.googleapis.com/auth/calendar.events.readonly https://www.googleapis.com/auth/tasks.readonly";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector stateProtector = protection.CreateProtector("izboard.google.sign-in.v1");
    private sealed record Pending(string Owner, string ConnectionId, string CredentialsVersion, string Verifier, long Timestamp);

    private static string Version(ProviderConnection connection) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(connection.ProtectedCredentials)));

    public (string Url, string State) Start(ProviderConnection connection)
    {
        var credentials = connections.Credentials(connection);
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        // The PKCE verifier stays confidential: the protected state is encrypted, not only signed.
        var state = stateProtector.Protect(JsonSerializer.Serialize(new Pending(
            connection.UserId.Value, connection.Id, Version(connection), verifier, clock.GetUtcNow().ToUnixTimeSeconds())));
        var url = QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string?>
        {
            ["client_id"] = credentials["clientId"], ["redirect_uri"] = credentials["redirectUri"],
            ["response_type"] = "code", ["scope"] = Scopes, ["access_type"] = "offline", ["prompt"] = "consent",
            ["state"] = state, ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256"
        });
        return (url, state);
    }

    public async Task<Result<bool>> CompleteAsync(string? state, string? cookie, string? code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(state) || state.Length > 4096 || string.IsNullOrEmpty(cookie) || state != cookie)
            return "Google sign-in expired or the browser does not match. Start again from Connections.";
        var pending = Decode(state);
        var age = pending is null ? -1 : clock.GetUtcNow().ToUnixTimeSeconds() - pending.Timestamp;
        if (pending is null || age < 0 || age > StateLifetime.TotalSeconds || string.IsNullOrWhiteSpace(code))
            return "Google sign-in was cancelled or expired.";
        var owner = new UserId(pending.Owner);
        var connection = connections.Find(pending.ConnectionId, owner);
        if (connection is null || Version(connection) != pending.CredentialsVersion) return "Connection changed. Start sign-in again.";
        var credentials = connections.Credentials(connection);
        var result = await TokenRequest(credentials, new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = credentials["redirectUri"], ["code_verifier"] = pending.Verifier
        }, ct);
        if (result.IsFailure) return result.Error;
        var token = result.Value;
        var granted = String(token, "scope").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Scopes.Split(' ').Any(scope => !granted.Contains(scope))) return "Grant access to both Calendar and Tasks, then reconnect.";
        var refreshToken = String(token, "refresh_token");
        if (string.IsNullOrEmpty(refreshToken)) return "Google did not return offline access. Reconnect and grant access again.";
        credentials[RefreshTokenKey] = refreshToken;
        // Recheck after the network request, so deleting/editing a connection invalidates the pending sign-in.
        var current = connections.Find(pending.ConnectionId, owner);
        if (current is null || Version(current) != pending.CredentialsVersion) return "Connection changed. Start sign-in again.";
        if (!connections.Update(connection, credentials)) return "Connection no longer exists.";
        return true;
    }

    public async Task<Result<string>> AccessTokenAsync(ProviderConnection connection, CancellationToken ct)
    {
        // The encrypted credential version prevents reuse after a connection is reconfigured.
        var key = "google-token:" + connection.Id + ":" + connection.ProtectedCredentials;
        if (cache.TryGetValue<string>(key, out var cached) && cached is not null) return Result.Success<string>(cached);
        var credentials = connections.Credentials(connection);
        if (!credentials.TryGetValue(RefreshTokenKey, out var refresh) || string.IsNullOrEmpty(refresh))
            return Result.Failure<string>("Connect this Google account from Connections first.");
        var result = await TokenRequest(credentials, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }, ct);
        if (result.IsFailure) return Result.Failure<string>(result.Error);
        var token = String(result.Value, "access_token");
        if (string.IsNullOrEmpty(token)) return Result.Failure<string>("Google returned no access token. Reconnect the account.");
        var seconds = result.Value.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var value) ? value : 300;
        cache.Set(key, token, TimeSpan.FromSeconds(Math.Clamp(seconds - 60, 1, 3600)));
        return Result.Success<string>(token);
    }

    private async Task<Result<JsonElement>> TokenRequest(Dictionary<string, string> credentials, Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = credentials["clientId"];
        form["client_secret"] = credentials["clientSecret"];
        try
        {
            using var response = await clients.CreateClient("provider-google").PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), ct);
            if (!response.IsSuccessStatusCode) return "Google authorization failed. Check your OAuth credentials and reconnect.";
            return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        { return "Google authorization is unavailable. Please try again."; }
    }

    private Pending? Decode(string state)
    {
        try { return JsonSerializer.Deserialize<Pending>(stateProtector.Unprotect(state)); }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException) { return null; }
    }

    internal static string String(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
