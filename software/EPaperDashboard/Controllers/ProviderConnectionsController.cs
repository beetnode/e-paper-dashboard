using EPaperDashboard.Guards;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EPaperDashboard.Controllers;

[ApiController, Authorize, Route("api/providers")]
public sealed class ProviderConnectionsController(ProviderRegistry registry, ProviderConnectionStore connections, Services.UserService users) : BaseApiController
{
    private bool HasAccount => CurrentUserId != UserId.Empty && (IsHomeAssistantIngress || users.GetUserById(CurrentUserId).HasValue);

    [HttpGet]
    public IActionResult Plugins() => Ok(new
    {
        data = registry.Plugins.Select(plugin => new
        {
            plugin.Manifest.Id, plugin.Manifest.Name, plugin.Manifest.Credentials, plugin.Manifest.RequiresOAuth,
            plugin.Manifest.Widgets, plugin.Manifest.SetupInstructions,
            SupportsSignIn = plugin is IOAuthDataProviderPlugin,
            SignInCredentialKeys = (plugin as IOAuthDataProviderPlugin)?.AuthorizationCredentialKeys ?? []
        })
    });

    [HttpGet("connections")]
    public IActionResult List() => HasAccount ? Ok(new { data = connections.List(CurrentUserId).Select(View) }) : Forbid();

    [HttpPost("connections")]
    public IActionResult Create(SaveProviderConnection request)
    {
        if (!HasAccount) return Forbid();
        var plugin = registry.Find(request.PluginId);
        if (plugin is null) return BadRequest(new { message = "Unknown provider" });
        if (connections.List(CurrentUserId).Count(c => !connections.IsDefault(c)) >= 20) return BadRequest(new { message = "You can create up to 20 connections." });
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
        if (connections.IsDefault(connection)) return BadRequest(new { message = DefaultConnectionIsFixed });
        var plugin = registry.Find(connection.PluginId);
        if (plugin is null || request.PluginId != connection.PluginId) return BadRequest(new { message = "The provider cannot be changed." });
        var credentials = connections.Credentials(connection);
        var changed = new HashSet<string>();
        foreach (var (key, value) in AllowedCredentials(plugin, request.Credentials))
            if (!string.IsNullOrWhiteSpace(value) && credentials.GetValueOrDefault(key) != value) { credentials[key] = value; changed.Add(key); }
        var error = Validate(plugin, request.Name, credentials);
        if (error is not null) return BadRequest(new { message = error });
        // Authorization belongs to the previous settings, unless this update supplies it directly.
        if (changed.Count > 0 && plugin is IOAuthDataProviderPlugin oauth)
            foreach (var key in oauth.AuthorizationCredentialKeys.Where(key => !changed.Contains(key))) credentials.Remove(key);
        connection.Name = request.Name.Trim();
        if (!connections.Update(connection, credentials)) return Conflict(new { message = "Connection changed. Reload and try again." });
        return Ok(new { data = View(connection) });
    }

    [HttpDelete("connections/{id}")]
    public IActionResult Delete(string id)
    {
        if (!HasAccount) return Forbid();
        var connection = connections.Find(id, CurrentUserId);
        if (connection is null) return NotFound();
        if (connections.IsDefault(connection)) return BadRequest(new { message = DefaultConnectionIsFixed });
        connections.Delete(id, CurrentUserId);
        return NoContent();
    }

    /// <summary>Checks that the connection works by discovering its sources, without touching any dashboard.</summary>
    [HttpPost("connections/{id}/test")]
    public async Task<IActionResult> Test(string id)
    {
        if (!HasAccount) return Forbid();
        var connection = connections.Find(id, CurrentUserId);
        if (connection is null) return NotFound();
        var plugin = registry.Find(connection.PluginId);
        if (plugin is null) return BadRequest(new { message = "This provider is no longer available." });
        try
        {
            var result = await plugin.DiscoverAsync(new ProviderContext("", connection), HttpContext.RequestAborted);
            return result.IsSuccess
                ? Ok(new { message = $"{connection.Name} works: {result.Value.Count} source{(result.Value.Count == 1 ? "" : "s")} available." })
                : BadRequest(new { message = $"{connection.Name}: {result.Error}" });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Provider exceptions can contain credentials or remote responses.
            return BadRequest(new { message = $"{connection.Name}: the provider is unavailable. Check the connection settings." });
        }
    }

    [HttpPost("connections/{id}/authorize")]
    public IActionResult Authorize(string id)
    {
        if (!HasAccount) return Forbid();
        var connection = connections.Find(id, CurrentUserId);
        if (connection is null) return NotFound();
        if (connections.IsDefault(connection)) return BadRequest(new { message = DefaultConnectionIsFixed });
        if (registry.Find(connection.PluginId) is not IOAuthDataProviderPlugin plugin)
            return BadRequest(new { message = "This provider does not use sign-in." });
        var start = plugin.StartAuthorization(connection, HttpContext);
        if (start.IsFailure) return BadRequest(new { message = start.Error });
        var (url, state) = start.Value;
        Response.Cookies.Append(StateCookie(plugin.Manifest.Id), state, new CookieOptions
        { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10), Path = "/", IsEssential = true });
        return Ok(new { authUrl = url });
    }

    [AllowAnonymous, HttpGet("{pluginId}/callback")]
    public async Task<IActionResult> Callback(string pluginId, [FromQuery] string? state, [FromQuery] string? code)
    {
        if (registry.Find(pluginId) is not IOAuthDataProviderPlugin plugin) return NotFound();
        var cookie = StateCookie(plugin.Manifest.Id);
        var result = await plugin.CompleteAuthorizationAsync(state, Request.Cookies[cookie], code, HttpContext.RequestAborted);
        Response.Cookies.Delete(cookie, new CookieOptions { Path = "/" });
        if (result.IsFailure) return LocalRedirect("~/connections?error=" + Uri.EscapeDataString(result.Error));
        return LocalRedirect("~/connections?connected=" + Uri.EscapeDataString(plugin.Manifest.Id));
    }

    private const string DefaultConnectionIsFixed = "This connection is provided by izBoard and cannot be changed or removed.";

    // Binds the one-time authorization state to the browser that started sign-in.
    internal static string StateCookie(string pluginId) => $"izboard-{pluginId}-oauth";

    private object View(ProviderConnection connection)
    {
        var plugin = registry.Find(connection.PluginId);
        var credentials = connections.Credentials(connection);
        var isDefault = connections.IsDefault(connection);
        return new
        {
            connection.Id, connection.Name, connection.PluginId,
            // The default connection is authorized by the deployment and has no editable credentials.
            isDefault,
            connected = isDefault || plugin is not IOAuthDataProviderPlugin oauth || oauth.IsAuthorized(credentials),
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
