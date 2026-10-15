using System.Security.Cryptography;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Firmware;

/// <summary>
/// Background service that periodically checks for firmware updates
/// and caches the latest firmware binary for OTA delivery to devices.
/// </summary>
public sealed class FirmwareUpdateService : BackgroundService
{
    private readonly IFirmwareReleaseProvider _provider;
    private readonly ILogger<FirmwareUpdateService> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly string _firmwareCacheDir;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private FirmwareReleaseInfo? _latestRelease;
    private string? _cachedBinaryPath;
    private CachedMd5? _cachedMd5;

    private sealed record CachedMd5(string Path, string Hash);

    public FirmwareUpdateService(
        IFirmwareReleaseProvider provider,
        IEnvironmentConfiguration environmentConfiguration,
        ILogger<FirmwareUpdateService> logger)
    {
        _provider = provider;
        _logger = logger;
        _checkInterval = environmentConfiguration.FirmwareCheckInterval;
        _firmwareCacheDir = Path.Combine(environmentConfiguration.ConfigDir, "firmware-cache");
        Directory.CreateDirectory(_firmwareCacheDir);
    }

    /// <summary>
    /// Gets the latest known firmware release information.
    /// Returns null if no release info has been fetched yet.
    /// </summary>
    public FirmwareReleaseInfo? GetLatestRelease() => _latestRelease;

    /// <summary>
    /// Whether the latest release can be delivered to devices right now: its binary is
    /// cached locally, so a device download is served from disk instead of waiting on GitHub.
    /// Both the device-facing version header and the UI's update badge depend on this.
    /// </summary>
    public bool IsOtaReady => GetReadyBinaryPath() is not null;

    /// <summary>
    /// Lowercase hex MD5 of the ready cached binary, or null when none is ready.
    /// Devices pass it to the OTA updater to verify the image before flashing.
    /// Computed once per cached binary.
    /// </summary>
    public string? GetFirmwareMd5()
    {
        var path = GetReadyBinaryPath();
        if (path is null)
            return null;

        var cached = _cachedMd5;
        if (cached?.Path == path)
            return cached.Hash;

        try
        {
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(MD5.HashData(stream));
            _cachedMd5 = new CachedMd5(path, hash);
            return hash;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to compute MD5 of cached firmware binary {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Gets the path to the cached firmware binary, downloading it if necessary.
    /// Returns null if no firmware is available.
    /// </summary>
    public async Task<string?> GetFirmwareBinaryPathAsync(CancellationToken cancellationToken = default)
    {
        var readyPath = GetReadyBinaryPath();
        if (readyPath is not null)
            return readyPath;

        var release = _latestRelease;
        if (release?.DownloadUrl is null)
            return null;

        // Download on demand
        await DownloadAndCacheBinaryAsync(release.DownloadUrl, release.Version, cancellationToken);
        return GetReadyBinaryPath();
    }

    /// <summary>
    /// Forces an immediate check for firmware updates.
    /// </summary>
    public async Task<FirmwareReleaseInfo?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await CheckForUpdatesAsync(cancellationToken);
            return _latestRelease;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Firmware Update Service started (check interval: {Interval})", _checkInterval);

        // Initial check after a short delay to allow app startup to complete
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _refreshLock.WaitAsync(stoppingToken);
                try
                {
                    await CheckForUpdatesAsync(stoppingToken);
                }
                finally
                {
                    _refreshLock.Release();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for firmware updates");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Firmware Update Service stopped");
    }

    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking for firmware updates...");

        var release = await _provider.GetLatestReleaseAsync(cancellationToken);
        if (release is null)
        {
            _logger.LogWarning("No firmware release information available from provider");
            return;
        }

        var isNewVersion = _latestRelease is null || _latestRelease.Version != release.Version;
        _latestRelease = release;

        if (isNewVersion)
            _logger.LogInformation("New firmware version detected: {Version}", release.Version);

        if (release.DownloadUrl is null)
        {
            _logger.LogWarning("Firmware release v{Version} has no binary asset; devices will not be offered an update", release.Version);
            return;
        }

        if (IsOtaReady)
        {
            _logger.LogInformation("Firmware is up to date: {Version}", release.Version);
            return;
        }

        // Check if binary is already cached on disk (e.g., from before a server restart)
        var expectedPath = GetCachePath(release.Version);
        if (File.Exists(expectedPath))
        {
            _cachedBinaryPath = expectedPath;
            _logger.LogInformation("Firmware binary v{Version} restored from disk cache", release.Version);
        }
        else
        {
            // Also retries a download that failed on an earlier check of the same version
            await DownloadAndCacheBinaryAsync(release.DownloadUrl, release.Version, cancellationToken);
        }
    }

    private string GetCachePath(string version) => Path.Combine(_firmwareCacheDir, $"firmware-{version}.bin");

    /// <summary>
    /// The cached binary path, only when it belongs to the latest release. A binary left over
    /// from an older release must never be served for a newer version: the device would flash
    /// it, still report the old version, and reflash on every wake.
    /// </summary>
    private string? GetReadyBinaryPath()
    {
        var release = _latestRelease;
        var path = _cachedBinaryPath;
        if (release?.DownloadUrl is null || path is null)
            return null;

        return path == GetCachePath(release.Version) && File.Exists(path) ? path : null;
    }

    private async Task<byte[]?> DownloadAndCacheBinaryAsync(string downloadUrl, string version, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Downloading firmware binary v{Version}...", version);

            var binary = await _provider.DownloadFirmwareBinaryAsync(downloadUrl, cancellationToken);
            if (binary is null)
            {
                _logger.LogWarning("Failed to download firmware binary");
                return null;
            }

            // Clean up old cached binaries
            foreach (var oldFile in Directory.GetFiles(_firmwareCacheDir, "*.bin"))
            {
                try { File.Delete(oldFile); }
                catch { /* best effort cleanup */ }
            }

            // Cache the new binary
            var cachePath = GetCachePath(version);
            _cachedMd5 = null;
            await File.WriteAllBytesAsync(cachePath, binary, cancellationToken);
            _cachedBinaryPath = cachePath;

            _logger.LogInformation("Firmware binary v{Version} cached successfully ({Size} bytes)", version, binary.Length);
            return binary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download and cache firmware binary v{Version}", version);
            return null;
        }
    }
}
