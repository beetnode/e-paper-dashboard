using EPaperDashboard.Authorization;
using EPaperDashboard.Services.Rendering;
using EPaperDashboard.Services.Providers;
using EPaperDashboard.Services.Plugins;
using EPaperDashboard.Services.Plugins.HomeAssistant;
using EPaperDashboard.Services.Ai;
using EPaperDashboard.Utilities;
using EPaperDashboard.Data;
using EPaperDashboard.Data.Repositories;
using Microsoft.AspNetCore.Authentication.Cookies;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Firmware;
using EPaperDashboard.Authentication;
using EPaperDashboard.Services.Ai.DataSections;
using EPaperDashboard.Services.Components.Playwright;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

// Maintenance is a separate process: no web host, background jobs, or deployment setup.
if (args.Contains("--maintenance"))
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("Use --maintenance without other command-line arguments");
        Environment.ExitCode = 1;
        return;
    }
    Environment.ExitCode = DatabaseMaintenanceCommand.Run(new EnvironmentConfigurationWrapper());
    return;
}

if (args.Contains("--users"))
{
    Environment.ExitCode = UserAdminCommand.Run(new EnvironmentConfigurationWrapper(), args);
    return;
}

// The same slim image runs the restricted sidecar, keeping both hosts on the exact app build.
// Exit before app configuration, databases, authentication, or application routes are initialized.
if (args.Contains("--rendering-host"))
{
    await RenderingComponentHost.RunAsync();
    return;
}
if (args.Contains("--rendering-health"))
{
    Environment.Exit(await new PlaywrightComponentRuntime().IsAvailableAsync() ? 0 : 1);
    return;
}

var builder = WebApplication.CreateBuilder(args);

var environmentConfiguration = new EnvironmentConfigurationWrapper();
builder.Services.AddSingleton<IEnvironmentConfiguration>(environmentConfiguration);
DatabaseSettings databaseSettings;
try
{
	databaseSettings = DatabaseSettings.Parse(
		environmentConfiguration.DatabaseProvider,
		environmentConfiguration.DatabaseConnectionString);
}
catch (ArgumentException exception)
{
	Console.Error.WriteLine($"Configuration Error: {exception.Message}");
	Environment.Exit(1);
	return;
}

var devicePort = builder.Configuration.GetValue<int>("DevicePort", Constants.DefaultDevicePort);
// The Angular dev server proxies ingress test requests from the local machine.
var allowLoopbackIngress = builder.Environment.IsDevelopment();

// Register deployment strategy based on APP_MODE via a single shared factory (used again below
// for pre-container validation, so the mode→type mapping only lives in one place).
builder.Services.AddSingleton<IDeploymentStrategy>(sp =>
	DeploymentStrategyFactory.Create(EnvironmentConfiguration.AppMode, environmentConfiguration,
		sp.GetRequiredService<ILoggerFactory>(), devicePort, allowLoopbackIngress));

// Validate configuration using strategy (no DI container yet, so use a throwaway null logger)
var validationStrategy = DeploymentStrategyFactory.Create(
	EnvironmentConfiguration.AppMode, environmentConfiguration, NullLoggerFactory.Instance, devicePort, allowLoopbackIngress);

var validationResult = validationStrategy.ValidateConfiguration();
if (validationResult.IsFailure)
{
	Console.Error.WriteLine($"Configuration Error: {validationResult.Error}");
	Environment.Exit(1);
}

// The device port speaks TLS only; devices pin this certificate when they pair.
var deviceCertificate = DeviceCertificateStore.LoadOrCreate(Path.Combine(environmentConfiguration.ConfigDir, "device-tls"));
builder.WebHost.ConfigureKestrel(options =>
	options.ConfigureHttpsDefaults(https => https.ServerCertificate = deviceCertificate));

var dataProtectionKeysDir = environmentConfiguration.DataProtectionKeysDir;
Directory.CreateDirectory(dataProtectionKeysDir);
builder.Services.AddDataProtection()
	.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers(options => options.Filters.Add<PasswordChangeRequiredFilter>())
	.AddJsonOptions(options =>
	{
		options.JsonSerializerOptions.Converters.Add(new TimeOnlyJsonConverter());
	});

builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	options.AddPolicy("AccountCreation", context => IpRateLimit(context, 10));
	options.AddPolicy("Login", context => IpRateLimit(context, 10));
	options.AddPolicy("PairingAnnounce", context => IpRateLimit(context, 10));
	options.AddPolicy("PairingStatus", context => IpRateLimit(context, 240));
	options.AddPolicy("PairingClaim", context => IpRateLimit(context, 10));
	options.OnRejected = (context, _) =>
	{
		context.HttpContext.Response.Headers.RetryAfter = "5";
		return ValueTask.CompletedTask;
	};

	static RateLimitPartition<string> IpRateLimit(HttpContext context, int permitsPerMinute) =>
		RateLimitPartition.GetFixedWindowLimiter(
			context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			_ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = permitsPerMinute,
				Window = TimeSpan.FromMinutes(1),
				QueueLimit = 0,
				AutoReplenishment = true
			});
});

#if DEBUG
builder.Services.AddSwaggerGen(options =>
{
	options.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
	{
		Description = "API Key needed to access the endpoints. X-Api-Key: {apiKey}",
		In = Microsoft.OpenApi.Models.ParameterLocation.Header,
		Name = "X-Api-Key",
		Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
		Scheme = "ApiKeyScheme"
	});
	options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
	{
		{
			new Microsoft.OpenApi.Models.OpenApiSecurityScheme
			{
				Reference = new Microsoft.OpenApi.Models.OpenApiReference
				{
					Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
					Id = "ApiKey"
				},
				Scheme = "ApiKeyScheme",
				Name = "X-Api-Key",
				In = Microsoft.OpenApi.Models.ParameterLocation.Header
			},
			new List<string>()
		}
	});
});
#endif

builder.Services
	.AddSingleton(TimeProvider.System)
	.AddMemoryCache()
	.AddTransient<IPageToImageRenderingService, PageToImageRenderingService>()
	.AddSingleton<PlaywrightComponentManager>()
	.AddSingleton<PlaywrightComponentRuntime>()
	.AddSingleton<UserRenderScheduler>()
	.AddSingleton<RenderingAccessService>()
	.AddHostedService(sp => sp.GetRequiredService<PlaywrightComponentManager>())
	.AddSingleton<IImageFactory, ImageFactory>()
	.AddSingleton<UserSessionValidator>()
	.AddSingleton<UserService>()
	.AddSingleton<SetupCodeProvider>()
	.AddSingleton<DashboardService>()
	.AddSingleton<DeviceService>()
	.AddSingleton<PairingService>()
	.AddDataProviderPlugins()
	.AddSingleton<IAiContentProvider, AiContentProvider>()
	.AddSingleton<IAiDataSectionFormatter, EntityStateSectionFormatter>()
	.AddSingleton<IAiDataSectionFormatter, EPaperDashboard.Services.Ai.DataSections.CalendarEventsSectionFormatter>()
	.AddSingleton<IAiDataSectionFormatter, EPaperDashboard.Services.Ai.DataSections.TodoItemsSectionFormatter>()
	.AddSingleton<IAiDataSectionFormatter, EPaperDashboard.Services.Ai.DataSections.WeatherForecastSectionFormatter>()
	.AddSingleton<IAiDataSectionFormatter, EPaperDashboard.Services.Ai.DataSections.RssFeedSectionFormatter>()
	.AddSingleton<ISsrDataProvider, SsrDataProvider>()
	.AddSingleton<FontAwesomeIconRegistry>()
	.AddSingleton<RenderingUtilities>(sp =>
		RenderingUtilities.Create(
			sp.GetRequiredService<IWebHostEnvironment>(),
			sp.GetRequiredService<FontAwesomeIconRegistry>()))
	.AddSingleton<DashboardImageRenderingService>()
	.AddSingleton<DesignerPreviewImageStore>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.HeaderWidgetRenderer>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.CalendarWidgetRenderer>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.WeatherWidgetRenderer>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.WeatherForecastWidgetRenderer>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.TodoWidgetRenderer>()
	.AddSingleton<EPaperDashboard.Services.Rendering.Widgets.MarkdownWidgetRenderer>()
	.AddSingleton<IWidgetRenderer>(sp => sp.GetRequiredService<EPaperDashboard.Services.Rendering.Widgets.MarkdownWidgetRenderer>())
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.AiContentWidgetRenderer>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.RssFeedWidgetRenderer>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.VersionWidgetRenderer>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.AppIconWidgetRenderer>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.ImageWidgetRenderer>()
	.AddSingleton<IWidgetRenderer, EPaperDashboard.Services.Rendering.Widgets.GraphWidgetRenderer>()
	.AddSingleton<IAiServiceFactory, AiServiceFactory>()
	.AddSingleton<AiPromptBuilder>()
	.AddSingleton<AiDataFetcher>()
	.AddSingleton<AiResponseParser>()
	.AddSingleton<WidgetValidator>()
	.AddSingleton<WidgetLayoutEngine>()
	.AddSingleton<GridPacker>()
	.AddSingleton<AiDashboardGenerationService>()
	.AddSingleton<ApiKeyPolicyEvaluator>()
	.AddSingleton<DeviceLastSeenTracker>()
	.AddHostedService<DashboardScheduleMonitorService>()
	.AddHostedService<AiPreGenerationService>();

builder.Services.AddPersistence(databaseSettings);

// Firmware update services
if (EnvironmentConfiguration.FirmwareUpdateEnabled)
{
	switch (EnvironmentConfiguration.FirmwareReleaseProvider.ToLowerInvariant())
	{
		case "github":
		default:
			builder.Services.AddSingleton<IFirmwareReleaseProvider, GitHubFirmwareReleaseProvider>();
			break;
	}
	builder.Services.AddSingleton<FirmwareUpdateService>();
	builder.Services.AddHostedService(sp => sp.GetRequiredService<FirmwareUpdateService>());
}

builder.Services.AddHttpClient(Constants.DashboardHttpClientName);
builder.Services.AddHttpClient(Constants.HassHttpClientName);
builder.Services.AddHttpClient(Constants.SsrImageHttpClientName, client =>
{
	client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient(Constants.FirmwareHttpClientName, client =>
{
	client.DefaultRequestHeaders.Add("User-Agent", $"{Constants.AppName}/{Constants.AppVersion}");
	client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
	client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient(Constants.PlaywrightComponentHttpClientName, client =>
{
	client.DefaultRequestHeaders.Add("User-Agent", $"{Constants.AppName}/{Constants.AppVersion}");
	client.Timeout = TimeSpan.FromMinutes(30);
});

// Requests that came through Home Assistant ingress use the add-on's shared user; everything else uses the cookie.
const string DefaultAuthenticationScheme = "Default";
builder.Services.AddAuthentication(DefaultAuthenticationScheme)
.AddPolicyScheme(DefaultAuthenticationScheme, null, options =>
{
	options.ForwardDefaultSelector = context =>
		context.RequestServices.GetRequiredService<IDeploymentStrategy>().IsIngressRequest(context)
			? HomeAssistantIngressAuthenticationHandler.SchemeName
			: CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
	options.LoginPath = "/Login";
	options.LogoutPath = "/Logout";
	options.AccessDeniedPath = "/AccessDenied";
	options.Cookie.HttpOnly = true;
	options.Cookie.SameSite = SameSiteMode.Lax;
	// Self-hosted instances often run plain HTTP on the LAN; behind TLS the cookie is marked Secure automatically.
	options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
	options.ExpireTimeSpan = TimeSpan.FromDays(14);
	options.SlidingExpiration = true;
	options.Events.OnRedirectToLogin = ReturnForbiddenInsteadOfRedirect;
	options.Events.OnRedirectToAccessDenied = ReturnForbiddenInsteadOfRedirect;
	options.Events.OnValidatePrincipal = async context =>
	{
		if (context.Principal is null) return;
		var check = context.HttpContext.RequestServices.GetRequiredService<UserSessionValidator>().Validate(context.Principal);
		switch (check.State)
		{
			case SessionState.Reject:
				context.RejectPrincipal();
				await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
				break;
			case SessionState.Refresh:
				context.ReplacePrincipal(check.Principal!);
				context.ShouldRenew = true;
				break;
		}
	};

	static Task ReturnForbiddenInsteadOfRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context)
	{
		if (context.Request.Path.StartsWithSegments("/api"))
		{
			context.Response.StatusCode = 403;
		}
		else
		{
			context.Response.Redirect(context.RedirectUri);
		}
		return Task.CompletedTask;
	}
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", null)
.AddScheme<AuthenticationSchemeOptions, HomeAssistantIngressAuthenticationHandler>(HomeAssistantIngressAuthenticationHandler.SchemeName, null);

builder.Services.AddAuthorizationBuilder()
	.AddPolicy("SuperUserOnly", policy => policy.RequireClaim(Constants.IsSuperUserClaim, "true"))
	.AddPolicy("ApiKeyPolicy", policy =>
	{
		policy.RequireAssertion(context =>
		{
			var httpContext = context.Resource as HttpContext
				?? (context.Resource as Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext)?.HttpContext;

			var evaluator = httpContext?.RequestServices.GetRequiredService<ApiKeyPolicyEvaluator>();
			return evaluator?.Evaluate(httpContext) ?? false;
		});
	});

builder.Services.Configure<RazorPagesOptions>(options =>
{
	options.Conventions.AllowAnonymousToPage("/Login");
	options.Conventions.AllowAnonymousToPage("/Register");
	options.Conventions.AllowAnonymousToPage("/AccessDenied");
	options.Conventions.AllowAnonymousToPage("/Privacy");
});

await using var app = builder.Build();

try
{
    app.Services.GetRequiredService<IPersistenceInitializer>().Initialize();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Database initialization failed: {exception.Message}");
    Environment.ExitCode = 1;
    return;
}

app.Services.GetRequiredService<LegacyHomeAssistantSettingsMigration>().Run();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
app.Services.GetRequiredService<SetupCodeProvider>().AnnounceIfPending(logger);
logger.LogInformation("Device port {DevicePort} serves TLS with certificate SHA-256 {Fingerprint}",
	devicePort, DeviceCertificateStore.Fingerprint(deviceCertificate));
var strategy = app.Services.GetRequiredService<IDeploymentStrategy>();

// Only loopback proxies are trusted: ingress requests are recognized by the Supervisor's own address.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
	ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

app.UseCors(builder => builder
	.AllowAnyOrigin()
	.AllowAnyMethod()
	.AllowAnyHeader());

#if DEBUG
if (app.Environment.IsDevelopment())
{
	app.UseSwagger().UseSwaggerUI();
}
#endif

if (!app.Environment.IsDevelopment())
{
	app.UseHsts();
}

strategy.UseBeforeRouting(app, app.Environment);

app.UseRouting();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
	var isDevicePort = context.Connection.LocalPort == devicePort;
	
	if (isDevicePort)
	{
		var endpoint = context.GetEndpoint();

		if (!EPaperDashboard.Guards.DeviceAccessGuard.IsAccessible(endpoint))
		{
			context.Response.StatusCode = 404;
			return;
		}

		if (EPaperDashboard.Guards.DeviceAccessGuard.RequiresActivePairing(endpoint))
		{
			var pairingService = context.RequestServices.GetRequiredService<PairingService>();
			pairingService.CleanupExpiredSessions();

			if (!pairingService.HasActiveSessions())
			{
				context.Response.StatusCode = 503;
				await context.Response.WriteAsync("Pairing service unavailable");
				return;
			}
		}

		// Add latest firmware version header to all device-port responses, but only once the
		// binary is cached: an uncached download can outlast the device's OTA timeout and
		// burn its limited retries for that version.
		var firmwareService = context.RequestServices.GetService<FirmwareUpdateService>();
		var latestRelease = firmwareService?.GetLatestRelease();
		if (latestRelease is not null && firmwareService!.IsOtaReady)
		{
			context.Response.OnStarting(() =>
			{
				context.Response.Headers[HttpHeaderNames.FirmwareVersionHeaderName] = latestRelease.Version;
				return Task.CompletedTask;
			});
		}

		// Track device firmware version and update last seen timestamp
		context.RequestServices.GetRequiredService<DeviceLastSeenTracker>().Record(
			context.RequestServices.GetRequiredService<DeviceService>(),
			context.Request.Headers[HttpHeaderNames.ApiKeyHeaderName].ToString(),
			context.Request.Headers[HttpHeaderNames.DeviceFirmwareVersionHeaderName].ToString());
	}

	await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

strategy.UseBeforeStaticFiles(app, app.Environment);

app.UseStaticFiles();

app.UseStaticFiles(new StaticFileOptions
{
	FileProvider = new PhysicalFileProvider(
		Path.Combine(builder.Environment.WebRootPath, "browser")),
	RequestPath = ""
});

app.UseSpa(spa =>
{
	spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions
	{
		FileProvider = new PhysicalFileProvider(
			Path.Combine(builder.Environment.WebRootPath, "browser"))
	};

	if (app.Environment.IsDevelopment())
	{
		spa.UseProxyToSpaDevelopmentServer("http://localhost:4200");
	}
});

app.Run();
public class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
	private const string Format = "HH:mm";

	public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var value = reader.GetString();
		if (string.IsNullOrEmpty(value))
		{
			return TimeOnly.MinValue;
		}
		if (TimeOnly.TryParseExact(value, Format, null, System.Globalization.DateTimeStyles.None, out var result))
		{
			return result;
		}
		return TimeOnly.Parse(value);
	}

	public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToString(Format));
	}
}
