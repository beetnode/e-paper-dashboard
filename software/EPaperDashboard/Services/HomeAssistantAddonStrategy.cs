using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using CSharpFunctionalExtensions;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;

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

    public Uri? GetOAuthClientUri(HttpContext? context = null)
    {
        if (context is not null && TryGetIngressPath(context, out var ingressPath))
        {
            var browserOrigin = context.Items["BrowserOrigin"]?.ToString()?.TrimEnd('/')
                ?? "http://homeassistant";
            var ingressUrl = $"{browserOrigin}{ingressPath}";
            _logger.LogDebug("Using ingress URL from request context: {IngressUrl}", ingressUrl);
            return new Uri(ingressUrl);
        }

        _logger.LogWarning("OAuth client URI not available - no ingress header in request context.");
        return null;
    }

    public void PerformInitialSetup(IServiceProvider serviceProvider)
    {
    }

    public void ApplyMiddleware(IApplicationBuilder app, IWebHostEnvironment environment)
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

    public void ApplyPostStaticFilesMiddleware(IApplicationBuilder app, IWebHostEnvironment environment)
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
