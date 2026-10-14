using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using CSharpFunctionalExtensions;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Primitives;

namespace EPaperDashboard.Services;

public class HomeAssistantAddonStrategy : IDeploymentStrategy
{
    private readonly ILogger<HomeAssistantAddonStrategy> _logger;
    private readonly IEnvironmentConfiguration _environmentConfiguration;
    private readonly int _devicePort;
    private readonly bool _allowLoopbackIngress;
    private readonly string _supervisorToken;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cachedIndexHtml = new();

    public HomeAssistantAddonStrategy(
        ILogger<HomeAssistantAddonStrategy> logger,
        IEnvironmentConfiguration environmentConfiguration,
        int devicePort = Constants.DefaultDevicePort,
        bool allowLoopbackIngress = false)
    {
        _logger = logger;
        _environmentConfiguration = environmentConfiguration;
        _devicePort = devicePort;
        _allowLoopbackIngress = allowLoopbackIngress;
        _supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN")
            ?? throw new InvalidOperationException("SUPERVISOR_TOKEN not found");
    }

    public DeploymentMode Mode => DeploymentMode.Addon;

    public bool IsUserManagementEnabled => false;

    // The supervisor proxies Home Assistant Core's API and WebSocket with a token it provides, but cannot issue
    // long-lived tokens: rendering a Home Assistant dashboard needs a connection with a user's own token.
    public HomeAssistantEndpoint? DefaultHomeAssistant => new(Constants.SupervisorCoreUrl, _supervisorToken, "/websocket");

    // Supervisor add-ons run as a single container, so there is no rendering sidecar.
    public bool RunsRendererInProcess => true;

    public string GetConfigDirectory() => _environmentConfiguration.ConfigDir;

    public UnitResult<string> ValidateConfiguration()
    {
        // Ingress is browser-only. The display needs a reachable device API URL
        // served through the add-on's exposed port.
        var clientUrlError = ClientUrlValidator.GetValidationError(_environmentConfiguration.ClientUri);
        if (clientUrlError is not null)
        {
            return UnitResult.Failure(clientUrlError);
        }

        return UnitResult.Success<string>();
    }

    public bool IsIngressRequest(HttpContext context) => TryGetIngressPath(context, out _);

    public Result<HomeAssistantOAuthClient, string> GetHomeAssistantOAuthClient(HttpContext? context)
    {
        if (context is null || !TryGetIngressPath(context, out var ingressPath) || BrowserOrigin(context) is not { } origin)
        {
            _logger.LogWarning("Home Assistant sign-in needs a request through ingress that names the browser's origin.");
            return "Could not determine ingress URL from request. Ensure you're accessing the addon through Home Assistant ingress.";
        }

        // Home Assistant accepts redirect URIs on the client ID's origin. Under ingress that origin is Home Assistant's.
        return new HomeAssistantOAuthClient(origin + ingressPath, origin);
    }

    /// <summary>
    /// The origin the browser uses for Home Assistant: browsers send it on the POST that starts sign-in, and
    /// Home Assistant forwards the scheme and host it was reached on.
    /// </summary>
    internal static string? BrowserOrigin(HttpContext context)
    {
        var headers = context.Request.Headers;
        var forwardedProto = FirstValue(headers["X-Forwarded-Proto"]);
        var forwardedHost = FirstValue(headers["X-Forwarded-Host"]);
        string?[] candidates =
        [
            headers.Origin.ToString(),
            forwardedProto is null || forwardedHost is null ? null : $"{forwardedProto}://{forwardedHost}"
        ];

        foreach (var candidate in candidates)
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                && string.IsNullOrEmpty(uri.UserInfo) && uri.PathAndQuery == "/" && string.IsNullOrEmpty(uri.Fragment))
            {
                return uri.GetLeftPart(UriPartial.Authority);
            }
        }

        return null;
    }

    private static string? FirstValue(StringValues values) =>
        values.ToString().Split(',')[0].Trim() is { Length: > 0 } value ? value : null;

    public void PerformInitialSetup(IServiceProvider serviceProvider)
    {
    }

    public void UseBeforeRouting(IApplicationBuilder app, IWebHostEnvironment environment)
    {
        app.Use(async (context, next) =>
        {
            if (!environment.IsDevelopment() && TryGetIngressPath(context, out var ingressPath))
            {
                RewriteIngressPath(context, ingressPath);
            }

            await next();
        });
    }

    public void UseBeforeStaticFiles(IApplicationBuilder app, IWebHostEnvironment environment)
    {
        app.Use(async (context, next) =>
        {
            if (!TryGetIngressPath(context, out var ingressPath))
            {
                await next();
                return;
            }

            var path = context.Request.Path.Value ?? "";

            // Skip API requests and static file requests (files with extensions)
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                (path.Contains('.') && !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
            {
                await next();
                return;
            }

            var html = _cachedIndexHtml.GetOrAdd(ingressPath, key =>
            {
                var indexPath = Path.Combine(environment.WebRootPath, "browser", "index.html");
                if (!File.Exists(indexPath))
                {
                    _logger.LogError("Index.html not found at {IndexPath}", indexPath);
                    return string.Empty;
                }

                return WithBaseHref(File.ReadAllText(indexPath), key);
            });

            if (string.IsNullOrEmpty(html))
            {
                await next();
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength = Encoding.UTF8.GetByteCount(html);
            await context.Response.WriteAsync(html);
        });
    }

    internal static void RewriteIngressPath(HttpContext context, string ingressPath)
    {
        var originalPath = context.Request.Path.Value ?? "/";
        context.Request.PathBase = new PathString(ingressPath);

        if (originalPath.StartsWith(ingressPath, StringComparison.OrdinalIgnoreCase))
        {
            var newPath = originalPath.Substring(ingressPath.Length);
            context.Request.Path = new PathString(string.IsNullOrEmpty(newPath) ? "/" : newPath);
        }
    }

    internal static string WithBaseHref(string indexHtml, string ingressPath) =>
        indexHtml.Replace("<base href=\"/\">", $"<base href=\"{HtmlEncoder.Default.Encode(ingressPath + "/")}\">");

    // Only a trusted request with a well-formed path counts as ingress, so the path is safe to cache and embed.
    private bool TryGetIngressPath(HttpContext context, [NotNullWhen(true)] out string? ingressPath)
    {
        ingressPath = null;
        return HomeAssistantIngress.IsTrusted(context, _devicePort, _allowLoopbackIngress)
            && HomeAssistantIngress.TryGetPath(context, out ingressPath);
    }
}
