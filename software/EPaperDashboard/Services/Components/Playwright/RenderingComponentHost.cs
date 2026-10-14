using EPaperDashboard.RenderingComponent;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Components.Playwright;

internal static class RenderingComponentHost
{
    private const string SocketPath = "/run/izboard-rendering/renderer.sock";
    private const string ComponentRoot = "/data/components/playwright";

    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The renderer container requires Linux.");
        Directory.CreateDirectory(Path.GetDirectoryName(SocketPath)!);
        // Docker init reaps children; stale sockets from a previous container are safe to replace.
        if (File.Exists(SocketPath)) File.Delete(SocketPath);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 8_000_000;
            options.ListenUnixSocket(SocketPath);
        });
        builder.Services.AddSingleton<PlaywrightComponentRuntime>();
        builder.Services.AddSingleton<RemoteRenderJobs>();
        var app = builder.Build();
        app.MapGet("/health", () => new RenderingHostHealth(
            Constants.AppVersion, PlaywrightComponentManager.GetRuntimeIdentifier(), RenderingComponentInfo.ProtocolVersion, true, true));
        app.MapPost("/drain", async (PlaywrightComponentRuntime runtime, HttpContext context) =>
        {
            using var lease = await runtime.AcquireExclusiveAsync(context.RequestAborted);
            return Results.Ok();
        });
        app.MapPost("/cancel/{id:guid}", async (Guid id, RemoteRenderJobs jobs, HttpContext context) =>
        {
            await jobs.CancelAsync(id).WaitAsync(context.RequestAborted);
            return Results.Ok();
        });
        app.MapPost("/render", async (RemoteRenderRequest message, PlaywrightComponentRuntime runtime, RemoteRenderJobs jobs, HttpContext context) =>
        {
            var job = jobs.Start(message.JobId, context.RequestAborted);
            if (job is null) return Results.Conflict();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(job.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(150));
            try
            {
                if (PlaywrightComponentManager.NormalizeVersion(message.AppVersion)
                    != PlaywrightComponentManager.NormalizeVersion(Constants.AppVersion))
                    return Results.Conflict();
                var directory = Path.GetFullPath(Path.Combine(ComponentRoot, message.ComponentDirectory));
                if (!directory.StartsWith(ComponentRoot + "/", StringComparison.Ordinal))
                    return Results.BadRequest();
                var relative = Path.GetRelativePath(ComponentRoot, directory);
                if (!(relative == "current" || (relative.StartsWith(".version-", StringComparison.Ordinal) && !relative.Contains('/'))
                    || (relative.StartsWith(".install-", StringComparison.Ordinal) && relative.Split('/') is [_, "extracted"])))
                    return Results.BadRequest();
                message.Request.Validate();
                using var lease = await runtime.AcquireRenderAsync(timeout.Token);
                var component = PlaywrightComponentManager.ReadAndValidateComponent(directory);
                var image = await runtime.RunLocalAsync(component, message.Request, timeout.Token, requireSandbox: true);
                return Results.Bytes(image, message.Request.Mode == "html" ? "image/png" : "image/jpeg");
            }
            catch (OperationCanceledException) { return Results.StatusCode(408); }
            catch (Exception exception)
            {
                // Do not log URLs, input HTML, tokens, or raw browser exceptions from a user's page.
                if (message.Request.Mode == "html" && message.Request.Width == 200 && message.Request.Height == 100
                    && message.Request.Html == "<!doctype html><html><body>izBoard</body></html>")
                {
                    app.Logger.LogWarning("Rendering readiness check failed: {Error}",
                        (exception as RenderWorkerFailedException)?.WorkerError ?? exception.Message);
                    return Results.StatusCode(500);
                }
                if (exception is RenderWorkerFailedException failure)
                {
                    // The sanitized reason is safe to log and to show to the dashboard owner.
                    app.Logger.LogWarning("Isolated rendering request failed: {Reason}", failure.Message);
                    // Opt-in operator diagnostics: raw errors can contain dashboard URLs or page text, never tokens.
                    if (PlaywrightComponentRuntime.LogWorkerErrors)
                        app.Logger.LogWarning("Rendering worker error: {WorkerError}", failure.WorkerError);
                    return Results.Json(new PlaywrightComponentRuntime.RenderFailureResponse(failure.Message), statusCode: 500);
                }
                app.Logger.LogWarning("Isolated rendering request failed ({ErrorType})", exception.GetType().Name);
                return Results.StatusCode(500);
            }
            finally { job.Complete(); }
        });
        await app.StartAsync();
        File.SetUnixFileMode(SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
        await app.WaitForShutdownAsync();
    }
}
