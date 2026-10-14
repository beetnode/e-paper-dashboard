using System.Text.Json;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Firmware;

/// <summary>
/// Firmware release provider that fetches releases from GitHub Releases API.
/// Firmware is released independently of the application under <c>firmware-v*</c> tags; older
/// application releases (<c>v*</c>) that still carry a firmware binary remain valid candidates.
/// Looks for binary assets matching the configured pattern (default: *.bin).
/// </summary>
public sealed class GitHubFirmwareReleaseProvider(
    IHttpClientFactory httpClientFactory,
    IEnvironmentConfiguration environmentConfiguration,
    ILogger<GitHubFirmwareReleaseProvider> logger) : IFirmwareReleaseProvider
{
    private const string FirmwareTagPrefix = "firmware-v";
    private const string LegacyTagPrefix = "v";
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
            // "Latest" is the application release, so firmware is chosen from the recent releases instead.
            var url = $"https://api.github.com/repos/{_repository}/releases?per_page=100";

            var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("GitHub API returned {StatusCode} for {Url}", response.StatusCode, url);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var release = SelectLatestRelease(doc.RootElement);
            if (release is null)
                logger.LogWarning("No firmware release found in GitHub repository {Repository}", _repository);
            else
                logger.LogInformation("Found GitHub firmware release: v{Version} (asset: {HasAsset})",
                    release.Version, release.DownloadUrl is not null ? "available" : "not found");
            return release;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch firmware releases from GitHub repository {Repository}", _repository);
            return null;
        }
    }

    internal FirmwareReleaseInfo? SelectLatestRelease(JsonElement releases)
    {
        FirmwareReleaseInfo? latest = null;
        Version? latestVersion = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (IsTrue(release, "draft") || IsTrue(release, "prerelease")) continue;
            var tagName = release.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            var isFirmwareTag = tagName.StartsWith(FirmwareTagPrefix, StringComparison.Ordinal);
            var versionText = isFirmwareTag ? tagName[FirmwareTagPrefix.Length..]
                : tagName.StartsWith(LegacyTagPrefix, StringComparison.OrdinalIgnoreCase) ? tagName[LegacyTagPrefix.Length..]
                : null;
            if (versionText is null || !Version.TryParse(versionText, out var version)) continue;

            var (downloadUrl, fileSize) = FindAsset(release);
            // Application releases stopped carrying firmware once it was released on its own.
            if (!isFirmwareTag && downloadUrl is null) continue;
            if (latestVersion is not null && version <= latestVersion) continue;

            var releaseNotes = release.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
            DateTimeOffset? publishedAt = release.TryGetProperty("published_at", out var pubEl)
                && DateTimeOffset.TryParse(pubEl.GetString(), out var dt) ? dt : null;
            latest = new FirmwareReleaseInfo(versionText, releaseNotes, publishedAt, downloadUrl, fileSize);
            latestVersion = version;
        }
        return latest;
    }

    private (string? DownloadUrl, long? FileSize) FindAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets)) return (null, null);
        foreach (var asset in assets.EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString() ?? "";
            if (MatchesAssetPattern(assetName))
                return (asset.GetProperty("browser_download_url").GetString(),
                    asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : null);
        }
        return (null, null);
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

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
