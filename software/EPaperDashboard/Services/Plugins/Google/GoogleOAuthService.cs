using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CSharpFunctionalExtensions;
using EPaperDashboard.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace EPaperDashboard.Services.Plugins.Google;

/// <summary>Per-connection OAuth clients, browser-bound one-time state, and encrypted refresh tokens.</summary>
public sealed class GoogleOAuthService(ProviderConnectionStore connections, IHttpClientFactory clients, IMemoryCache cache)
{
    public const string RefreshTokenKey = "refreshToken";
    private const string Scopes = "https://www.googleapis.com/auth/calendar.calendarlist.readonly https://www.googleapis.com/auth/calendar.events.readonly https://www.googleapis.com/auth/tasks.readonly";
    private readonly object stateLock = new();
    private sealed record Pending(UserId Owner, string ConnectionId, string CredentialsVersion, string Verifier);

    public (string Url, string State) Start(ProviderConnection connection)
    {
        var credentials = connections.Credentials(connection);
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        cache.Set("google-oauth:" + state, new Pending(connection.UserId, connection.Id, connection.ProtectedCredentials, verifier), TimeSpan.FromMinutes(10));
        var url = QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string?>
        {
            ["client_id"] = credentials["clientId"], ["redirect_uri"] = credentials["redirectUri"],
            ["response_type"] = "code", ["scope"] = Scopes, ["access_type"] = "offline", ["prompt"] = "consent",
            ["state"] = state, ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256"
        });
        return (url, state);
    }

    public async Task<Result<bool, string>> CompleteAsync(string? state, string? cookie, string? code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(state) || state.Length > 128 || string.IsNullOrEmpty(cookie) || state != cookie)
            return "Google sign-in expired or the browser does not match. Start again from Connections.";
        Pending? pending;
        lock (stateLock)
        {
            cache.TryGetValue("google-oauth:" + state, out pending);
            cache.Remove("google-oauth:" + state);
        }
        if (pending is null || string.IsNullOrWhiteSpace(code)) return "Google sign-in was cancelled or expired.";
        var connection = connections.Find(pending.ConnectionId, pending.Owner);
        if (connection is null || connection.ProtectedCredentials != pending.CredentialsVersion) return "Connection changed. Start sign-in again.";
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
        var current = connections.Find(pending.ConnectionId, pending.Owner);
        if (current is null || current.ProtectedCredentials != pending.CredentialsVersion) return "Connection changed. Start sign-in again.";
        if (!connections.Update(connection, credentials)) return "Connection no longer exists.";
        return true;
    }

    public async Task<Result<string, string>> AccessTokenAsync(ProviderConnection connection, CancellationToken ct)
    {
        // The encrypted credential version prevents reuse after a connection is reconfigured.
        var key = "google-token:" + connection.Id + ":" + connection.ProtectedCredentials;
        if (cache.TryGetValue<string>(key, out var cached) && cached is not null) return Result.Success<string, string>(cached);
        var credentials = connections.Credentials(connection);
        if (!credentials.TryGetValue(RefreshTokenKey, out var refresh) || string.IsNullOrEmpty(refresh))
            return Result.Failure<string, string>("Connect this Google account from Connections first.");
        var result = await TokenRequest(credentials, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }, ct);
        if (result.IsFailure) return Result.Failure<string, string>(result.Error);
        var token = String(result.Value, "access_token");
        if (string.IsNullOrEmpty(token)) return Result.Failure<string, string>("Google returned no access token. Reconnect the account.");
        var seconds = result.Value.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var value) ? value : 300;
        cache.Set(key, token, TimeSpan.FromSeconds(Math.Clamp(seconds - 60, 1, 3600)));
        return Result.Success<string, string>(token);
    }

    private async Task<Result<JsonElement, string>> TokenRequest(Dictionary<string, string> credentials, Dictionary<string, string> form, CancellationToken ct)
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

    internal static string String(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
