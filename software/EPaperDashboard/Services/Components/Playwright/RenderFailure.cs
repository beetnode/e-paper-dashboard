using System.Text.RegularExpressions;

namespace EPaperDashboard.Services.Components.Playwright;

/// <summary>
/// Turns a browser worker error into a reason that is safe to show and log. Raw worker errors can contain
/// dashboard URLs or page content, so only recognized categories and Chromium network error codes are kept.
/// </summary>
/// <summary>A browser worker failure: <see cref="Exception.Message"/> is the sanitized reason.</summary>
internal sealed class RenderWorkerFailedException(string? workerError)
    : InvalidOperationException(RenderFailure.Describe(workerError))
{
    /// <summary>Unsanitized worker output; only for operator diagnostics of fixed, app-generated content.</summary>
    public string? WorkerError { get; } = workerError;
}

internal static partial class RenderFailure
{
    public const string Generic = "The rendering component could not render the dashboard.";

    public static string Describe(string? workerError)
    {
        if (string.IsNullOrWhiteSpace(workerError)) return Generic;
        var networkError = NetworkErrorPattern().Match(workerError);
        if (networkError.Success)
        {
            return networkError.Value switch
            {
                "net::ERR_CONNECTION_REFUSED" =>
                    "The renderer could not connect to the Home Assistant server (connection refused). " +
                    "Use a server URL the renderer can reach; 'localhost' refers to the renderer itself.",
                "net::ERR_NAME_NOT_RESOLVED" =>
                    "The renderer could not resolve the Home Assistant server name. Use a server URL the renderer can reach.",
                _ => $"The renderer could not load the Home Assistant page ({networkError.Value["net::".Length..]})."
            };
        }
        if (workerError.Contains("interrupted by another navigation", StringComparison.Ordinal))
            return "Home Assistant redirected while the dashboard was loading. Check the dashboard path.";
        if (workerError.Contains("crashed", StringComparison.OrdinalIgnoreCase)
            || workerError.Contains("has been closed", StringComparison.Ordinal))
            return "The browser stopped while rendering the dashboard. The page may need more resources than the renderer allows.";
        if (workerError.Contains("Timeout", StringComparison.Ordinal))
            return "Home Assistant did not finish loading in time. Check that the server and dashboard path respond.";
        if (workerError.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
            return "The browser sandbox could not start in the renderer. Check the renderer container's security settings.";
        return Generic;
    }

    [GeneratedRegex(@"net::ERR_[A-Z_]{1,64}")]
    private static partial Regex NetworkErrorPattern();
}
