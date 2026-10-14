using System.Text.Json;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Firmware;

/// <summary>
/// Firmware release provider that fetches releases from GitHub Releases API.
/// Looks for binary assets matching the configured pattern (default: *.bin).
/// </summary>
public sealed class GitHubFirmwareReleaseProvider(
    IHttpClientFactory httpClientFactory,
    IEnvironmentConfiguration environmentConfiguration,
    ILogger<GitHubFirmwareReleaseProvider> logger) : IFirmwareReleaseProvider
{
    private readonly string _repository = environmentConfiguration.FirmwareGitHubRepo;
    private readonly string _assetPattern = environmentConfiguration.FirmwareAssetPattern;

    public async Task<FirmwareReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_repository))
        {
            logger.LogWarning("GitHub repository not configured for firmware updates (FIRMWARE_GITHUB_REPO)");
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient(Constants.FirmwareHttpClientName);
            var url = $"https://api.github.com/repos/{_repository}/releases/latest";

            var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("GitHub API returned {StatusCode} for {Url}", response.StatusCode, url);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var version = tagName.TrimStart('v', 'V');

            var releaseNotes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

            DateTimeOffset? publishedAt = null;
            if (root.TryGetProperty("published_at", out var pubEl) &&
                DateTimeOffset.TryParse(pubEl.GetString(), out var dt))
            {
                publishedAt = dt;
            }

            // Find firmware binary asset matching the configured pattern
            string? downloadUrl = null;
            long? fileSize = null;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var assetName = asset.GetProperty("name").GetString() ?? "";
                    if (MatchesAssetPattern(assetName))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        fileSize = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : null;
                        break;
                    }
                }
            }

            logger.LogInformation("Found GitHub release: v{Version} (asset: {HasAsset})",
                version, downloadUrl is not null ? "available" : "not found");

            return new FirmwareReleaseInfo(version, releaseNotes, publishedAt, downloadUrl, fileSize);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch latest release from GitHub repository {Repository}", _repository);
            return null;
        }
    }

    public async Task<byte[]?> DownloadFirmwareBinaryAsync(string downloadUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(Constants.FirmwareHttpClientName);
            var response = await client.GetAsync(downloadUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to download firmware binary from {Url}: {StatusCode}", downloadUrl, response.StatusCode);
                return null;
            }

            var binary = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            logger.LogInformation("Downloaded firmware binary ({Size} bytes) from {Url}", binary.Length, downloadUrl);
            return binary;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download firmware binary from {Url}", downloadUrl);
            return null;
        }
    }

    internal bool MatchesAssetPattern(string assetName)
    {
        // Simple glob matching: *.bin matches any file ending in .bin
        if (_assetPattern.StartsWith("*."))
        {
            var extension = _assetPattern[1..]; // e.g., ".bin"
            return assetName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(assetName, _assetPattern, StringComparison.OrdinalIgnoreCase);
    }
}
