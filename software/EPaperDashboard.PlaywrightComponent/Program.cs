using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using EPaperDashboard.RenderingComponent;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

try
{
    ApplyComponentEnvironment();
    var input = await Console.In.ReadToEndAsync();
    var request = JsonSerializer.Deserialize<RenderRequest>(input, jsonOptions)
        ?? throw new InvalidOperationException("The render request was empty or invalid.");

    request.Validate();
    if (string.IsNullOrWhiteSpace(request.OutputPath) || !Path.IsPathFullyQualified(request.OutputPath))
        throw new InvalidOperationException("The output path must be absolute.");

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(GetLaunchOptions());
    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = request.Width, Height = request.Height }
    });
    var page = await context.NewPageAsync();

    if (request.Mode == "dashboard")
    {
        await RenderDashboardAsync(page, request);
    }
    else
    {
        await page.SetContentAsync(request.Html!, new PageSetContentOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 10_000
        });
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = request.OutputPath,
            Type = ScreenshotType.Png
        });
    }

    await Console.Out.WriteAsync(JsonSerializer.Serialize(new RenderResponse(true, null, 2), jsonOptions));
    return 0;
}
catch (Exception exception)
{
    await Console.Out.WriteAsync(JsonSerializer.Serialize(
        new RenderResponse(false, exception.Message, 2), jsonOptions));
    return 1;
}

static void ApplyComponentEnvironment()
{
    CopyEnvironmentVariable("IZBOARD_PLAYWRIGHT_LIBRARY_PATH", "LD_LIBRARY_PATH");
    CopyEnvironmentVariable("IZBOARD_PLAYWRIGHT_DATA_PATH", "XDG_DATA_DIRS");
    CopyEnvironmentVariable("IZBOARD_PLAYWRIGHT_FONTCONFIG_PATH", "FONTCONFIG_PATH");

    static void CopyEnvironmentVariable(string source, string destination)
    {
        var value = Environment.GetEnvironmentVariable(source);
        if (!string.IsNullOrWhiteSpace(value))
            Environment.SetEnvironmentVariable(destination, value);
    }
}

static async Task RenderDashboardAsync(IPage page, RenderRequest request)
{
    var dashboardUri = new Uri(request.DashboardUri!, UriKind.Absolute);
    var rootUri = new Uri(dashboardUri.GetLeftPart(UriPartial.Authority));

    await page.GotoAsync(rootUri.AbsoluteUri, NavigationOptions());
    await WaitForPageAsync(page);

    var hassTokens = JsonSerializer.Serialize(new
    {
        access_token = request.AccessToken,
        token_type = request.TokenType,
        hassUrl = request.HassUrl,
        clientId = request.ClientId,
        refresh_token = "",
        expires_in = 315360000,
        expires = DateTimeOffset.UtcNow.AddYears(10).ToUnixTimeMilliseconds()
    });
    await page.EvaluateAsync("token => localStorage.setItem('hassTokens', token)", hassTokens);

    await page.GotoAsync(dashboardUri.AbsoluteUri, NavigationOptions());
    await WaitForPageAsync(page);
    await page.ScreenshotAsync(new PageScreenshotOptions
    {
        Path = request.OutputPath,
        Type = ScreenshotType.Jpeg
    });
}

static PageGotoOptions NavigationOptions() => new()
{
    Timeout = 60_000,
    WaitUntil = WaitUntilState.NetworkIdle
};

static async Task WaitForPageAsync(IPage page)
{
    await page.WaitForLoadStateAsync(LoadState.Load);
    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
}

static BrowserTypeLaunchOptions GetLaunchOptions()
{
    var options = new BrowserTypeLaunchOptions { Headless = true };
    if (Environment.GetEnvironmentVariable("IZBOARD_REQUIRE_BROWSER_SANDBOX") == "1")
    {
        options.ChromiumSandbox = true;
        return options;
    }
    if (OperatingSystem.IsLinux())
    {
        // This compatibility mode is not a security boundary for untrusted dashboard content.
        options.Args = ["--no-sandbox", "--disable-setuid-sandbox"];
    }

    return options;
}
