using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Net.Sockets;
using EPaperDashboard.RenderingComponent;
using EPaperDashboard.Services.Rendering;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Components.Playwright;

// One shared runtime coordinates renders with activation and removal of their files.
public sealed class PlaywrightComponentRuntime
{
    private readonly int _concurrency;
    private readonly int _maximumQueuedRequests;
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _exclusiveOperations = new(1, 1);
    private readonly string _socketPath;
    private readonly string _componentRoot;
    private readonly bool _allowLocal;
    private int _queuedRequests;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PlaywrightComponentRuntime(
        IConfiguration? configuration = null,
        IEnvironmentConfiguration? environment = null,
        IDeploymentStrategy? deploymentStrategy = null)
    {
        _concurrency = ReadLimit(configuration, "RENDERING_MAX_CONCURRENT_RENDERS", 2, 1, 16);
        _maximumQueuedRequests = ReadLimit(configuration, "RENDERING_MAX_QUEUED_RENDERS", 100, 0, 1000);
        _slots = new SemaphoreSlim(_concurrency, _concurrency);
        _socketPath = configuration?["RENDERING_COMPONENT_SOCKET"] ?? "/run/izboard-rendering/renderer.sock";
        _componentRoot = Path.Combine(environment?.ConfigDir ?? "/data", "components", "playwright");
        _allowLocal = deploymentStrategy?.RunsRendererInProcess == true;
    }

    private static int ReadLimit(IConfiguration? configuration, string key, int fallback, int minimum, int maximum)
    {
        var value = configuration?[key];
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!int.TryParse(value, out var limit) || limit < minimum || limit > maximum)
            throw new InvalidOperationException($"{key} must be between {minimum} and {maximum}.");
        return limit;
    }

    internal async Task<IDisposable> AcquireRenderAsync(CancellationToken cancellationToken)
    {
        if (_slots.Wait(0, cancellationToken)) return new Lease(() => _slots.Release());
        if (Interlocked.Increment(ref _queuedRequests) > _maximumQueuedRequests)
        {
            Interlocked.Decrement(ref _queuedRequests);
            throw new InvalidOperationException("The rendering queue is full. Please try again later.");
        }
        try
        {
            await _slots.WaitAsync(cancellationToken);
            return new Lease(() => _slots.Release());
        }
        finally
        {
            Interlocked.Decrement(ref _queuedRequests);
        }
    }

    internal async Task<IDisposable> AcquireExclusiveAsync(CancellationToken cancellationToken)
    {
        await _exclusiveOperations.WaitAsync(cancellationToken);
        var acquired = 0;
        try
        {
            for (; acquired < _concurrency; acquired++)
                await _slots.WaitAsync(cancellationToken);
            return new Lease(() => { _slots.Release(_concurrency); _exclusiveOperations.Release(); });
        }
        catch
        {
            if (acquired > 0) _slots.Release(acquired);
            _exclusiveOperations.Release();
            throw;
        }
    }

    // Caller holds either a render or exclusive lease for the entire operation.
    internal async Task<byte[]> RunAsync(
        ActivePlaywrightComponent component, RenderRequest request, CancellationToken cancellationToken)
    {
        if (_allowLocal) return await RunLocalAsync(component, request, cancellationToken, requireSandbox: false);
        request.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(150));
        using var client = CreateSocketClient(_socketPath);
        await VerifyHostAsync(client, timeout.Token);
        var jobId = Guid.NewGuid();
        using var message = new HttpRequestMessage(HttpMethod.Post, "http://renderer/render")
        {
            Content = JsonContent.Create(new RemoteRenderRequest(
                Path.GetRelativePath(_componentRoot, component.RootPath), Constants.AppVersion,
                request with { OutputPath = null }, jobId))
        };
        HttpResponseMessage response;
        try { response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch
        {
            // Wait only for this worker; cancellation must not drain other users' renders.
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(160));
            using var cancellation = await client.PostAsync($"http://renderer/cancel/{jobId}", null, cleanupTimeout.Token);
            cancellation.EnsureSuccessStatusCode();
            throw;
        }
        using var responseLease = response;
        if (!response.IsSuccessStatusCode)
            throw new RenderingRequestException(await ReadFailureAsync(response, timeout.Token)
                ?? "The isolated rendering component failed. Check renderer container logs.");
        if (response.Content.Headers.ContentLength is not (> 0 and <= RenderRequest.MaximumImageBytes))
            throw new InvalidOperationException("The rendering component produced a missing or oversized image.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (output.Length + count > RenderRequest.MaximumImageBytes)
                throw new InvalidOperationException("The rendering component image exceeds the allowed size.");
            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
        }
        return output.ToArray();
    }

    // The renderer reports only reasons produced by RenderFailure, never raw browser errors.
    private static async Task<string?> ReadFailureAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var failure = await response.Content.ReadFromJsonAsync<RenderFailureResponse>(token);
            return failure?.Error is { Length: > 0 and <= 500 } error ? error : null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or HttpRequestException) { return null; }
    }

    internal sealed record RenderFailureResponse(string? Error);

    internal async Task DrainRemoteAsync(CancellationToken cancellationToken)
    {
        if (_allowLocal || !File.Exists(_socketPath)) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(160));
        using var client = CreateSocketClient(_socketPath);
        try
        {
            using var response = await client.PostAsync("http://renderer/drain", null, timeout.Token);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException exception) when (exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
        {
            // A stopped sidecar cannot execute new renders; Docker terminates its child processes.
        }
    }

    internal async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_allowLocal) return true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var client = CreateSocketClient(_socketPath);
            await VerifyHostAsync(client, timeout.Token);
            return true;
        }
        catch { return false; }
    }

    internal static HttpClient CreateSocketClient(string socketPath) => new(new SocketsHttpHandler
    {
        ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        },
        UseProxy = false
    }) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 64_000 };

    private static async Task VerifyHostAsync(HttpClient client, CancellationToken token)
    {
        using var response = await client.GetAsync("http://renderer/health", token);
        response.EnsureSuccessStatusCode();
        var health = await response.Content.ReadFromJsonAsync<RenderingHostHealth>(token);
        if (health is null || health.ProtocolVersion != RenderingComponentInfo.ProtocolVersion || !health.SandboxRequired || !health.SupportsJobCancellation
            || health.RuntimeIdentifier != PlaywrightComponentManager.GetRuntimeIdentifier()
            || PlaywrightComponentManager.NormalizeVersion(health.AppVersion) != PlaywrightComponentManager.NormalizeVersion(Constants.AppVersion))
            throw new InvalidOperationException("The renderer container must match the application build and require browser sandboxing.");
    }

    internal async Task<byte[]> RunLocalAsync(
        ActivePlaywrightComponent component, RenderRequest request, CancellationToken cancellationToken, bool requireSandbox)
    {
        request.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(150));
        var token = timeout.Token;
        var directory = Path.Combine(Path.GetTempPath(), $"izboard-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var outputPath = Path.Combine(directory, "image");
        using var process = new Process { StartInfo = CreateStartInfo(component, directory, requireSandbox) };
        var started = false;
        Task<string>? output = null;
        Task<string>? error = null;
        try
        {
            started = process.Start();
            if (!started) throw new InvalidOperationException("The rendering component could not be started.");
            output = ReadBoundedAsync(process.StandardOutput, token);
            error = ReadBoundedAsync(process.StandardError, token, keepTail: true);
            await process.StandardInput.WriteAsync(
                JsonSerializer.Serialize(request with { OutputPath = outputPath }, JsonOptions).AsMemory(), token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            var response = JsonSerializer.Deserialize<RenderResponse>(await output, JsonOptions);
            var browserLog = await error;
            if (response?.ProtocolVersion != RenderingComponentInfo.ProtocolVersion)
                throw new InvalidOperationException("The rendering component protocol is incompatible.");
            if (process.ExitCode != 0 || response.Success != true)
                throw new RenderWorkerFailedException(LogWorkerErrors && browserLog.Length > 0
                    ? $"{response.Error}\nBrowser log:\n{browserLog[Math.Max(0, browserLog.Length - 8000)..]}"
                    : response.Error);
            var file = new FileInfo(outputPath);
            if (!file.Exists || file.Length <= 0 || file.Length > RenderRequest.MaximumImageBytes)
                throw new InvalidOperationException("The rendering component produced a missing or oversized image.");
            return await File.ReadAllBytesAsync(outputPath, token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The rendering component exceeded its 150 second timeout.");
        }
        finally
        {
            // Covers stdin failures, invalid responses, cancellation, and timeout alike.
            try
            {
                if (started)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { /* exited during the check */ }
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            finally
            {
                timeout.Cancel();
                if (output is not null) { try { await output; } catch { /* observed during cleanup */ } }
                if (error is not null) { try { await error; } catch { /* observed during cleanup */ } }
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Operator opt-in; raw worker errors can contain dashboard URLs or page text.</summary>
    internal static bool LogWorkerErrors => Environment.GetEnvironmentVariable("RENDERING_LOG_WORKER_ERRORS") == "1";

    // Diagnostic streams keep their most recent output instead of failing the render.
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token, bool keepTail = false)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (text.Length + count > 64_000)
            {
                if (!keepTail) throw new InvalidOperationException("The rendering component response exceeds the allowed size.");
                text.Remove(0, text.Length + count - 64_000);
            }
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    private static ProcessStartInfo CreateStartInfo(ActivePlaywrightComponent component, string temporaryDirectory, bool requireSandbox)
    {
        var info = new ProcessStartInfo
        {
            FileName = "dotnet", WorkingDirectory = component.RootPath,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add(component.WorkerAssemblyPath);
        // Do not pass application configuration, signing keys, or other users' credentials.
        info.Environment.Clear();
        foreach (var key in new[] { "PATH", "DOTNET_ROOT", "LANG", "LC_ALL", "TZ", "SSL_CERT_FILE", "SSL_CERT_DIR" })
            if (Environment.GetEnvironmentVariable(key) is { } value) info.Environment[key] = value;
        info.Environment["HOME"] = temporaryDirectory;
        info.Environment["TMPDIR"] = temporaryDirectory;
        info.Environment["DOTNET_EnableDiagnostics"] = "0";
        info.Environment["IZBOARD_REQUIRE_BROWSER_SANDBOX"] = requireSandbox ? "1" : "0";
        // Opt-in diagnostics: Playwright writes the browser's own output to the worker's error stream.
        if (LogWorkerErrors) info.Environment["DEBUG"] = "pw:browser";
        info.Environment["PLAYWRIGHT_BROWSERS_PATH"] = component.BrowserPath;
        info.Environment["IZBOARD_PLAYWRIGHT_LIBRARY_PATH"] = component.LibraryPath;
        if (component.DataPath is not null) info.Environment["IZBOARD_PLAYWRIGHT_DATA_PATH"] = component.DataPath;
        if (component.FontConfigPath is not null) info.Environment["IZBOARD_PLAYWRIGHT_FONTCONFIG_PATH"] = component.FontConfigPath;
        return info;
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
