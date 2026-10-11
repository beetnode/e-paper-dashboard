using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using EPaperDashboard.Utilities;
using EPaperDashboard.RenderingComponent;

namespace EPaperDashboard.Services.Components.Playwright;

public sealed class PlaywrightComponentManager : BackgroundService
{
    private const int ManifestSchemaVersion = 2;
    private const long MaximumComponentBytes = 1_500_000_000;
    private const long MaximumExtractedBytes = 3_000_000_000;
    private const string DefaultRepository = "izdev-digital/e-paper-dashboard";
    private static readonly TimeSpan UpdateRetryInterval = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlaywrightComponentManager> _logger;
    private readonly string _componentRoot;
    private readonly string _repository;
    private readonly string _releaseTag;
    private readonly Uri? _componentBaseUri;
    private readonly object _statusLock = new();
    private readonly PlaywrightComponentRuntime _runtime;
    private readonly CancellationTokenSource _shutdown = new();
    private volatile bool _uninstalling;
    private Task? _installationTask;
    private PlaywrightComponentStatus _status;

    public PlaywrightComponentManager(
        IHttpClientFactory httpClientFactory,
        IEnvironmentConfiguration environmentConfiguration,
        IConfiguration configuration,
        ILogger<PlaywrightComponentManager> logger,
        PlaywrightComponentRuntime? runtime = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _runtime = runtime ?? new PlaywrightComponentRuntime();
        _componentRoot = Path.Combine(environmentConfiguration.ConfigDir, "components", "playwright");
        _repository = configuration["RENDERING_COMPONENT_REPOSITORY"]
            ?? configuration["PLAYWRIGHT_COMPONENT_REPOSITORY"]
            ?? DefaultRepository;
        var releaseTag = configuration["RENDERING_COMPONENT_RELEASE_TAG"]
            ?? configuration["PLAYWRIGHT_COMPONENT_RELEASE_TAG"];
        _releaseTag = string.IsNullOrWhiteSpace(releaseTag) ? GetDefaultReleaseTag(Constants.AppVersion) : releaseTag;
        _componentBaseUri = ParseComponentBaseUri(
            configuration["RENDERING_COMPONENT_BASE_URL"]
            ?? configuration["PLAYWRIGHT_COMPONENT_BASE_URL"]);
        CleanupInterruptedInstallations();
        _status = ReadInstalledStatus();
    }

    public PlaywrightComponentStatus GetStatus()
    {
        lock (_statusLock)
        {
            return _status;
        }
    }

    public bool StartInstallation(bool onlyIfNeeded = false)
    {
        if (!IsRuntimeSupported())
            throw new PlatformNotSupportedException("The rendering component is currently available for Linux x64 and arm64 only.");

        lock (_statusLock)
        {
            if (onlyIfNeeded && _status.State == PlaywrightComponentState.Installed) return false;
            if (_uninstalling || _installationTask is { IsCompleted: false })
                return false;

            _status = NewStatus(PlaywrightComponentState.Installing);
            _installationTask = InstallCoreAsync();
            return true;
        }
    }

    public async Task UninstallAsync()
    {
        lock (_statusLock)
        {
            if (_uninstalling || _installationTask is { IsCompleted: false })
                throw new InvalidOperationException("The rendering component is currently being installed.");
            _uninstalling = true;
        }
        try
        {
            using var lease = await _runtime.AcquireExclusiveAsync(_shutdown.Token);
            await _runtime.DrainRemoteAsync(_shutdown.Token);
            // The rename records removal before deletion. A crash cannot resurrect the component.
            var removedPath = _componentRoot + ".removing";
            if (Directory.Exists(removedPath)) Directory.Delete(removedPath, recursive: true);
            if (Directory.Exists(_componentRoot)) Directory.Move(_componentRoot, removedPath);
            SetStatus(NewStatus(PlaywrightComponentState.NotInstalled));
            if (Directory.Exists(removedPath)) Directory.Delete(removedPath, recursive: true);
            _logger.LogInformation("Uninstalled the rendering component");
        }
        finally
        {
            lock (_statusLock) _uninstalling = false;
        }
    }

    internal bool ShouldAutomaticallyUpdate()
    {
        var status = GetStatus();
        return IsRuntimeSupported()
            && (File.Exists(Path.Combine(_componentRoot, "requested")) || Directory.Exists(GetActiveDirectory()))
            && status.State is PlaywrightComponentState.Incompatible or PlaywrightComponentState.Failed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (ShouldAutomaticallyUpdate() && StartInstallation())
                    _logger.LogInformation("Updating the installed rendering component to match izBoard {AppVersion}", Constants.AppVersion);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not start the rendering component update");
            }

            try
            {
                await Task.Delay(UpdateRetryInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown.Cancel();
        await base.StopAsync(cancellationToken);
        Task? installation;
        lock (_statusLock) installation = _installationTask;
        if (installation is not null) await installation.WaitAsync(cancellationToken);
    }

    internal ActivePlaywrightComponent? GetActiveComponent()
    {
        var status = GetStatus();
        if (status.State != PlaywrightComponentState.Installed || _uninstalling)
            return null;

        try
        {
            return ReadAndValidateComponent(GetActiveDirectory());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The installed rendering component is invalid");
            SetStatus(NewStatus(PlaywrightComponentState.Failed, error: exception.Message));
            return null;
        }
    }

    private async Task InstallCoreAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        var token = timeout.Token;
        var operationDirectory = Path.Combine(_componentRoot, $".install-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(operationDirectory, "component.tar.gz");
        var extractPath = Path.Combine(operationDirectory, "extracted");

        try
        {
            Directory.CreateDirectory(_componentRoot);
            await File.WriteAllTextAsync(Path.Combine(_componentRoot, "requested"), Constants.AppVersion, token);
            Directory.CreateDirectory(operationDirectory);
            var runtimeIdentifier = GetRuntimeIdentifier();
            var assetName = $"rendering-component-{runtimeIdentifier}.tar.gz";
            var (archiveUrl, checksumUrl) = await ResolveAssetUrlsAsync(assetName, token);

            var expectedChecksum = await DownloadChecksumAsync(checksumUrl, token);
            await DownloadArchiveAsync(archiveUrl, archivePath, token);
            await VerifyChecksumAsync(archivePath, expectedChecksum, token);

            Directory.CreateDirectory(extractPath);
            await ExtractArchiveAsync(archivePath, extractPath, token);
            var candidate = ReadAndValidateComponent(extractPath);
            using (await _runtime.AcquireExclusiveAsync(token))
            {
                var image = await _runtime.RunAsync(candidate, new RenderRequest(
                    "html", 200, 100, Html: "<!doctype html><html><body>izBoard</body></html>"), token);
                if (image.Length < 8 || !image.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new InvalidOperationException("The rendering component readiness test failed.");
                Activate(extractPath);
            }

            var active = ReadAndValidateComponent(GetActiveDirectory());
            SetStatus(NewStatus(
                PlaywrightComponentState.Installed,
                installedVersion: active.Manifest.ComponentVersion));
            _logger.LogInformation(
                "Installed rendering component {ComponentVersion} for {RuntimeIdentifier}",
                active.Manifest.ComponentVersion,
                runtimeIdentifier);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to install the rendering component");
            var previous = ReadInstalledStatus();
            SetStatus(previous.State == PlaywrightComponentState.Installed
                ? previous with { Error = "The replacement could not be installed; the existing component remains ready." }
                : previous with { State = PlaywrightComponentState.Failed, Error = exception.Message });
        }
        finally
        {
            if (Directory.Exists(operationDirectory))
            {
                try
                {
                    Directory.Delete(operationDirectory, recursive: true);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Could not remove temporary component files at {Path}", operationDirectory);
                }
            }
        }
    }

    private async Task<(string ArchiveUrl, string ChecksumUrl)> ResolveAssetUrlsAsync(string assetName, CancellationToken token)
    {
        if (_componentBaseUri is not null)
        {
            _logger.LogInformation("Installing the rendering component from {BaseUri}", _componentBaseUri);
            return (
                new Uri(_componentBaseUri, Uri.EscapeDataString(assetName)).AbsoluteUri,
                new Uri(_componentBaseUri, Uri.EscapeDataString($"{assetName}.sha256")).AbsoluteUri);
        }

        var release = await GetReleaseAsync(token);
        return (
            FindAssetUrl(release, assetName),
            FindAssetUrl(release, $"{assetName}.sha256"));
    }

    private async Task<JsonElement> GetReleaseAsync(CancellationToken token)
    {
        var client = _httpClientFactory.CreateClient(Constants.PlaywrightComponentHttpClientName);
        var url = $"https://api.github.com/repos/{_repository}/releases/tags/{Uri.EscapeDataString(_releaseTag)}";
        using var response = await client.GetAsync(url, token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub release '{_releaseTag}' is unavailable ({(int)response.StatusCode}).");

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        return document.RootElement.Clone();
    }

    private static string FindAssetUrl(JsonElement release, string assetName)
    {
        if (release.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name)
                    && string.Equals(name.GetString(), assetName, StringComparison.Ordinal)
                    && asset.TryGetProperty("browser_download_url", out var url)
                    && !string.IsNullOrWhiteSpace(url.GetString()))
                    return url.GetString()!;
            }
        }

        throw new InvalidOperationException($"Release '{release.GetProperty("tag_name").GetString()}' does not contain {assetName}.");
    }

    private async Task<string> DownloadChecksumAsync(string url, CancellationToken token)
    {
        var client = _httpClientFactory.CreateClient(Constants.PlaywrightComponentHttpClientName);
        var checksumText = await client.GetStringAsync(url, token);
        var checksum = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (checksum is null || checksum.Length != 64 || !checksum.All(Uri.IsHexDigit))
            throw new InvalidOperationException("The component checksum is invalid.");
        return checksum.ToUpperInvariant();
    }

    private async Task DownloadArchiveAsync(string url, string destination, CancellationToken token)
    {
        var client = _httpClientFactory.CreateClient(Constants.PlaywrightComponentHttpClientName);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        if (totalBytes is > MaximumComponentBytes)
            throw new InvalidOperationException("The component archive exceeds the allowed size.");

        UpdateProgress(0, totalBytes);
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, true);
        var buffer = new byte[81_920];
        long downloaded = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read == 0)
                break;

            downloaded += read;
            if (downloaded > MaximumComponentBytes)
                throw new InvalidOperationException("The component archive exceeds the allowed size.");

            await output.WriteAsync(buffer.AsMemory(0, read), token);
            UpdateProgress(downloaded, totalBytes);
        }
    }

    private static async Task VerifyChecksumAsync(string path, string expectedChecksum, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var actualChecksum = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualChecksum),
                Convert.FromHexString(expectedChecksum)))
            throw new InvalidOperationException("The downloaded component failed checksum verification.");
    }

    private static async Task ExtractArchiveAsync(string archivePath, string destination, CancellationToken token)
    {
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        await using var file = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        long extractedBytes = 0;

        while (await reader.GetNextEntryAsync(cancellationToken: token) is { } entry)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destination, entry.Name));
            if (!string.Equals(destinationPath, destinationRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)
                && !destinationPath.StartsWith(destinationRoot, StringComparison.Ordinal))
                throw new InvalidOperationException("The component archive contains an unsafe path.");

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(destinationPath);
                    break;
                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    extractedBytes += entry.Length;
                    if (extractedBytes > MaximumExtractedBytes)
                        throw new InvalidOperationException("The extracted component exceeds the allowed size.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    await using (var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write))
                    {
                        if (entry.DataStream is not null)
                            await entry.DataStream.CopyToAsync(output, token);
                    }
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(destinationPath, entry.Mode);
                    break;
                default:
                    throw new InvalidOperationException($"The component archive contains unsupported entry type {entry.EntryType}.");
            }
        }
    }

    private void Activate(string extractedPath)
    {
        var activePath = GetActiveDirectory();
        var versionDirectory = $".version-{NormalizeVersion(Constants.AppVersion)}-{Guid.NewGuid():N}";
        var newPath = Path.Combine(_componentRoot, versionDirectory);
        var pointerPath = Path.Combine(_componentRoot, $".active-{Guid.NewGuid():N}");
        Directory.Move(extractedPath, newPath);
        try
        {
            File.WriteAllText(pointerPath, versionDirectory);
            File.Move(pointerPath, Path.Combine(_componentRoot, "active"), overwrite: true);
        }
        catch
        {
            Directory.Delete(newPath, recursive: true);
            throw;
        }
        // Pointer replacement is atomic; interrupted cleanup is completed at the next startup.
        if (Directory.Exists(activePath) && activePath != newPath)
            TryDeleteDirectory(activePath);
    }

    private PlaywrightComponentStatus ReadInstalledStatus()
    {
        try
        {
            if (!Directory.Exists(GetActiveDirectory()))
                return File.Exists(Path.Combine(_componentRoot, "requested"))
                    ? NewStatus(PlaywrightComponentState.Failed, error: "The requested rendering component is not ready. Installation will be retried.")
                    : NewStatus(PlaywrightComponentState.NotInstalled);
            var active = ReadAndValidateComponent(GetActiveDirectory(), requireCompatibleVersion: false);
            return IsVersionCompatible(active.Manifest)
                ? NewStatus(PlaywrightComponentState.Installed, installedVersion: active.Manifest.ComponentVersion)
                : NewStatus(
                    PlaywrightComponentState.Incompatible,
                    installedVersion: active.Manifest.ComponentVersion,
                    error: $"Component version {active.Manifest.ComponentVersion} does not match izBoard {Constants.AppVersion}.");
        }
        catch (Exception exception)
        {
            return NewStatus(PlaywrightComponentState.Failed, error: exception.Message);
        }
    }

    internal static ActivePlaywrightComponent ReadAndValidateComponent(string root, bool requireCompatibleVersion = true)
    {
        var manifestPath = Path.Combine(root, "component-manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("The component manifest is missing.");

        var manifest = JsonSerializer.Deserialize<PlaywrightComponentManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidOperationException("The component manifest is invalid.");
        if (manifest.SchemaVersion != ManifestSchemaVersion)
            throw new InvalidOperationException($"Unsupported component manifest schema {manifest.SchemaVersion}.");
        if (!string.Equals(manifest.RuntimeIdentifier, GetRuntimeIdentifier(), StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The component targets {manifest.RuntimeIdentifier}, not {GetRuntimeIdentifier()}.");
        if (requireCompatibleVersion && !IsVersionCompatible(manifest))
            throw new InvalidOperationException(
                $"Component version {manifest.ComponentVersion} does not match izBoard {Constants.AppVersion}.");

        var worker = ResolveComponentPath(root, manifest.WorkerAssembly);
        var browser = ResolveComponentPath(root, manifest.BrowserPath);
        var libraries = ResolveComponentPath(root, manifest.LibraryPath);
        var data = manifest.DataPath is null ? null : ResolveComponentPath(root, manifest.DataPath);
        var fontConfig = manifest.FontConfigPath is null ? null : ResolveComponentPath(root, manifest.FontConfigPath);
        if (!File.Exists(worker) || !Directory.Exists(browser) || !Directory.Exists(libraries)
            || (data is not null && !Directory.Exists(data))
            || (fontConfig is not null && !Directory.Exists(fontConfig)))
            throw new InvalidOperationException("The component payload is incomplete.");

        return new ActivePlaywrightComponent(root, worker, browser, libraries, data, fontConfig, manifest);
    }

    private static bool IsVersionCompatible(PlaywrightComponentManifest manifest) =>
        string.Equals(manifest.AppVersion, GetCompatibilityVersion(Constants.AppVersion), StringComparison.Ordinal)
        && string.Equals(NormalizeVersion(manifest.ComponentVersion), NormalizeVersion(Constants.AppVersion), StringComparison.Ordinal);

    private static string ResolveComponentPath(string root, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
            throw new InvalidOperationException("Component paths must be relative.");

        var rootPrefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The component manifest contains an unsafe path.");
        return fullPath;
    }

    private string GetActiveDirectory()
    {
        var pointer = Path.Combine(_componentRoot, "active");
        if (!File.Exists(pointer)) return Path.Combine(_componentRoot, "current");
        var directory = File.ReadAllText(pointer).Trim();
        if (!directory.StartsWith(".version-", StringComparison.Ordinal)
            || directory != Path.GetFileName(directory) || directory.Contains('/') || directory.Contains('\\'))
            throw new InvalidOperationException("The rendering component activation record is invalid.");
        return Path.Combine(_componentRoot, directory);
    }

    private void CleanupInterruptedInstallations()
    {
        try
        {
            if (Directory.Exists(_componentRoot + ".removing"))
                TryDeleteDirectory(_componentRoot + ".removing");
            if (!Directory.Exists(_componentRoot))
                return;
            // Recover installations made by the older two-directory activation mechanism.
            var active = GetActiveDirectory();
            var previous = Directory.GetDirectories(_componentRoot, ".previous-*");
            if (!Directory.Exists(active) && previous.Length == 1)
                Directory.Move(previous[0], active);
            foreach (var directory in Directory.EnumerateDirectories(_componentRoot))
            {
                var name = Path.GetFileName(directory);
                if (directory != active && (name.StartsWith(".install-") || name.StartsWith(".previous-")
                    || name.StartsWith(".version-") || name == "current"))
                    TryDeleteDirectory(directory);
            }
            foreach (var file in Directory.EnumerateFiles(_componentRoot, ".active-*")) File.Delete(file);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not inspect interrupted rendering component installations");
        }
    }

    private void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception exception)
        { _logger.LogWarning(exception, "Could not remove unused rendering component files at {Path}", directory); }
    }

    private void SetStatus(PlaywrightComponentStatus status)
    {
        lock (_statusLock)
        {
            _status = status;
        }
    }

    private void UpdateProgress(long bytesDownloaded, long? totalBytes)
    {
        lock (_statusLock)
        {
            _status = _status with { BytesDownloaded = bytesDownloaded, TotalBytes = totalBytes };
        }
    }

    private static PlaywrightComponentStatus NewStatus(
        PlaywrightComponentState state,
        string? installedVersion = null,
        string? error = null) =>
        new(state, Constants.AppVersion, GetRuntimeIdentifier(), IsRuntimeSupported(), installedVersion, Error: error);

    internal static string GetDefaultReleaseTag(string appVersion)
    {
        return $"rendering-v{NormalizeVersion(appVersion)}";
    }

    internal static string NormalizeVersion(string version)
    {
        var clean = version.Split('+', 2)[0];
        return Version.TryParse(clean, out var parsed) && parsed.Build >= 0
            ? $"{parsed.Major}.{parsed.Minor}.{parsed.Build}.{Math.Max(0, parsed.Revision)}" : clean;
    }

    internal static string GetCompatibilityVersion(string appVersion)
    {
        var cleanVersion = appVersion.Split('+', 2)[0];
        if (!Version.TryParse(cleanVersion, out var version) || version.Build < 0)
            return cleanVersion;
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    internal static string GetRuntimeIdentifier()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };
        var os = OperatingSystem.IsLinux()
            ? "linux"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : OperatingSystem.IsWindows() ? "windows" : "unsupported";
        return $"{os}-{architecture}";
    }

    private static Uri? ParseComponentBaseUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate($"{value.TrimEnd('/')}/", UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The rendering component base URL must be an absolute HTTP or HTTPS URL.");

        return uri;
    }

    private static bool IsRuntimeSupported() =>
        OperatingSystem.IsLinux()
        && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
}
