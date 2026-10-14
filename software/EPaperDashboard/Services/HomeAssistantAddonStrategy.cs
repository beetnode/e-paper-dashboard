using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using CSharpFunctionalExtensions;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.FileProviders;

namespace EPaperDashboard.Services;

public class HomeAssistantAddonStrategy : IDeploymentStrategy
{
    private readonly ILogger<HomeAssistantAddonStrategy> _logger;
    private readonly IEnvironmentConfiguration _environmentConfiguration;
    private readonly string _supervisorToken;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cachedIndexHtml = new();

    public HomeAssistantAddonStrategy(
        ILogger<HomeAssistantAddonStrategy> logger,
        IEnvironmentConfiguration environmentConfiguration)
    {
        _logger = logger;
        _environmentConfiguration = environmentConfiguration;
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

    public Uri? GetOAuthClientUri(HttpContext? context = null)
    {
        if (context?.Request.Headers.TryGetValue(Constants.IngressPathHeader, out var ingressPathValues) == true)
        {
            var ingressPath = ingressPathValues.ToString();
            if (!string.IsNullOrWhiteSpace(ingressPath))
            {
                var browserOrigin = context.Items["BrowserOrigin"]?.ToString()?.TrimEnd('/')
                    ?? "http://homeassistant";
                var ingressUrl = $"{browserOrigin}{ingressPath.TrimEnd('/')}";
                _logger.LogDebug("Using ingress URL from request context: {IngressUrl}", ingressUrl);
                return new Uri(ingressUrl);
            }
        }

        _logger.LogWarning("OAuth client URI not available - no ingress header in request context.");
        return null;
    }

    public ClaimsPrincipal? AuthenticateViaIngress(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey(Constants.IngressPathHeader))
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, Constants.HomeAssistantVirtualUserId.ToString()),
            new Claim(ClaimTypes.Name, Constants.HomeAssistantAdminUserName),
            new Claim(Constants.IsSuperUserClaim, "true"),
            new Claim(Constants.HomeAssistantIngressClaim, "true")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public Task<bool> ProcessIngressPathAsync(HttpContext context, IWebHostEnvironment environment)
    {
        if (!context.Request.Headers.TryGetValue(Constants.IngressPathHeader, out var ingressPathValues))
        {
            return Task.FromResult(false);
        }

        var ingressPath = ingressPathValues.ToString();
        if (string.IsNullOrWhiteSpace(ingressPath))
        {
            return Task.FromResult(false);
        }

        if (!ingressPath.StartsWith('/'))
        {
            ingressPath = "/" + ingressPath;
        }

        ingressPath = ingressPath.TrimEnd('/');
        
        var originalPath = context.Request.Path.Value ?? "/";
        context.Request.PathBase = new PathString(ingressPath);
        
        if (originalPath.StartsWith(ingressPath, StringComparison.OrdinalIgnoreCase))
        {
            var newPath = originalPath.Substring(ingressPath.Length);
            if (string.IsNullOrEmpty(newPath))
            {
                newPath = "/";
            }
            context.Request.Path = new PathString(newPath);
        }
        
        context.Items["IngressPath"] = ingressPath;
        
        return Task.FromResult(false);
    }

    public void PerformInitialSetup(IServiceProvider serviceProvider)
    {
    }

    public void ApplyMiddleware(IApplicationBuilder app, IWebHostEnvironment environment)
    {
        app.Use(async (context, next) =>
        {
            if (environment.IsDevelopment())
            {
                await next();
                return;
            }

            await ProcessIngressPathAsync(context, environment);
            await next();
        });
    }

    public void ApplyPostAuthenticationMiddleware(IApplicationBuilder app, IWebHostEnvironment environment)
    {
        app.Use(async (context, next) =>
        {
            var principal = AuthenticateViaIngress(context);
            if (principal == null)
            {
                await next();
                return;
            }

            context.User = principal;

            if (IsUserManagementEnabled)
            {
                await next();
                return;
            }

            var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
            var isUserManagementEndpoint = path.StartsWith("/api/auth/") || path.StartsWith("/api/users/");
            
            var isSetupStatus = path == "/api/auth/setup" && HttpMethods.IsGet(context.Request.Method);
            if (isUserManagementEndpoint && path != "/api/auth/current" && !isSetupStatus)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { message = "User management is disabled in Home Assistant add-on mode" });
                return;
            }
            
            await next();
        });
    }

    public void ApplyPostStaticFilesMiddleware(IApplicationBuilder app, IWebHostEnvironment environment)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Headers.TryGetValue(Constants.IngressPathHeader, out var headerValue))
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

            var ingressPath = headerValue.ToString();
            if (string.IsNullOrWhiteSpace(ingressPath))
            {
                await next();
                return;
            }
            
            if (!ingressPath.StartsWith('/'))
            {
                ingressPath = "/" + ingressPath;
            }
            ingressPath = ingressPath.TrimEnd('/');

            var html = _cachedIndexHtml.GetOrAdd(ingressPath, key =>
            {
                var indexPath = Path.Combine(environment.WebRootPath, "browser", "index.html");
                if (!File.Exists(indexPath))
                {
                    _logger.LogError("Index.html not found at {IndexPath}", indexPath);
                    return string.Empty;
                }

                var content = File.ReadAllText(indexPath);
                var baseHref = key + "/";
                return content.Replace("<base href=\"/\">", $"<base href=\"{baseHref}\">");
            });

            if (string.IsNullOrEmpty(html))
            {
                await next();
                return;
            }
            
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength = Encoding.UTF8.GetByteCount(html);
            await context.Response.WriteAsync(html);
            return; // Don't call next() after serving the response
        });
    }
}
