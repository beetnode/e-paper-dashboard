using EPaperDashboard.Guards;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Plugins;
using EPaperDashboard.Services.Plugins.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EPaperDashboard.Controllers;

[ApiController, Authorize, Route("api/providers")]
public sealed class ProviderConnectionsController(ProviderRegistry registry, ProviderConnectionStore connections, GoogleOAuthService google, Services.UserService users) : BaseApiController
{
    private bool HasAccount => CurrentUserId != UserId.Empty && (IsHomeAssistantIngress || users.GetUserById(CurrentUserId).HasValue);

    [HttpGet]
    public IActionResult Plugins() => Ok(new { data = registry.Manifests });

    [HttpGet("connections")]
    public IActionResult List() => HasAccount ? Ok(new { data = connections.List(CurrentUserId).Select(View) }) : Forbid();

    [HttpPost("connections")]
    public IActionResult Create(SaveProviderConnection request)
    {
        if (!HasAccount) return Forbid();
        var plugin = registry.Find(request.PluginId);
        if (plugin is null) return BadRequest(new { message = "Unknown provider" });
        if (connections.List(CurrentUserId).Count >= 20) return BadRequest(new { message = "You can create up to 20 connections." });
        var credentials = AllowedCredentials(plugin, request.Credentials);
        var error = Validate(plugin, request.Name, credentials);
        if (error is not null) return BadRequest(new { message = error });
        var connection = new ProviderConnection { UserId = CurrentUserId, Name = request.Name.Trim(), PluginId = plugin.Manifest.Id };
        connections.Create(connection, credentials);
        return Ok(new { data = View(connection) });
    }

    [HttpPut("connections/{id}")]
    public IActionResult Update(string id, SaveProviderConnection request)
    {
        if (!HasAccount) return Forbid();
        var connection = connections.Find(id, CurrentUserId);
        if (connection is null) return NotFound();
        var plugin = registry.Find(connection.PluginId);
        if (plugin is null || request.PluginId != connection.PluginId) return BadRequest(new { message = "The provider cannot be changed." });
        var credentials = connections.Credentials(connection);
        var changed = false;
        foreach (var (key, value) in AllowedCredentials(plugin, request.Credentials))
            if (!string.IsNullOrWhiteSpace(value) && credentials.GetValueOrDefault(key) != value) { credentials[key] = value; changed = true; }
        var error = Validate(plugin, request.Name, credentials);
        if (error is not null) return BadRequest(new { message = error });
        if (changed) credentials.Remove("refreshToken");
        connection.Name = request.Name.Trim();
        if (!connections.Update(connection, credentials)) return Conflict(new { message = "Connection changed. Reload and try again." });
        return Ok(new { data = View(connection) });
    }

    [HttpDelete("connections/{id}")]
    public IActionResult Delete(string id)
    {
        if (!HasAccount) return Forbid();
        if (connections.Find(id, CurrentUserId) is null) return NotFound();
        connections.Delete(id, CurrentUserId);
        return NoContent();
    }

    [HttpPost("connections/{id}/authorize")]
    public IActionResult AuthorizeGoogle(string id)
    {
        if (!HasAccount) return Forbid();
        var connection = connections.Find(id, CurrentUserId);
        if (connection is null) return NotFound();
        if (connection.PluginId != "google") return BadRequest(new { message = "This provider does not use Google sign-in." });
        var (url, state) = google.Start(connection);
        Response.Cookies.Append(GoogleOAuthService.CookieName, state, new CookieOptions
        { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10), Path = "/", IsEssential = true });
        return Ok(new { authUrl = url });
    }

    [AllowAnonymous, HttpGet("google/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? state, [FromQuery] string? code)
    {
        var result = await google.CompleteAsync(state, Request.Cookies[GoogleOAuthService.CookieName], code, HttpContext.RequestAborted);
        Response.Cookies.Delete(GoogleOAuthService.CookieName, new CookieOptions { Path = "/" });
        if (result.IsFailure) return LocalRedirect("~/connections?error=" + Uri.EscapeDataString(result.Error));
        return LocalRedirect("~/connections?connected=google");
    }

    private object View(ProviderConnection connection)
    {
        var plugin = registry.Find(connection.PluginId);
        var credentials = connections.Credentials(connection);
        return new
        {
            connection.Id, connection.Name, connection.PluginId,
            connected = plugin?.Manifest.RequiresOAuth != true || credentials.ContainsKey("refreshToken"),
            credentials = plugin?.Manifest.Credentials.Where(field => !field.Secret)
                .ToDictionary(field => field.Key, field => credentials.GetValueOrDefault(field.Key) ?? "")
        };
    }
    private static Dictionary<string, string> AllowedCredentials(IDataProviderPlugin plugin, Dictionary<string, string>? input) =>
        (input ?? []).Where(pair => plugin.Manifest.Credentials.Any(field => field.Key == pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value?.Trim() ?? "");
    private static string? Validate(IDataProviderPlugin plugin, string? name, Dictionary<string, string> credentials) =>
        string.IsNullOrWhiteSpace(name) || name.Length > 100 ? "Connection name must contain 1–100 characters."
        : credentials.Values.Any(value => value.Length > 8192) ? "Credential value is too long." : plugin.ValidateCredentials(credentials);
}

public sealed record SaveProviderConnection(string PluginId, string Name, Dictionary<string, string>? Credentials);

[ApiController, Authorize, DashboardOwner, Route("api/dashboards/{dashboardId}/sources")]
public sealed class DashboardSourcesController(PluginDataRouter router) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Sources(string dashboardId)
    {
        var result = await router.DiscoverAsync(dashboardId, HttpContext.RequestAborted);
        return Ok(new { data = result.Sources, errors = result.Errors });
    }
}
