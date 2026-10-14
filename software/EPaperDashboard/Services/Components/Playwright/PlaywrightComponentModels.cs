using System.Text.Json.Serialization;

namespace EPaperDashboard.Services.Components.Playwright;

[JsonConverter(typeof(JsonStringEnumConverter<PlaywrightComponentState>))]
public enum PlaywrightComponentState
{
    NotInstalled,
    Installing,
    Installed,
    Incompatible,
    Failed
}

public sealed record PlaywrightComponentStatus(
    PlaywrightComponentState State,
    string AppVersion,
    string RuntimeIdentifier,
    bool Supported,
    string? InstalledVersion = null,
    long BytesDownloaded = 0,
    long? TotalBytes = null,
    string? Error = null,
    bool Ready = false);

internal sealed record PlaywrightComponentManifest(
    int SchemaVersion,
    string AppVersion,
    string ComponentVersion,
    string RuntimeIdentifier,
    string WorkerAssembly,
    string BrowserPath,
    string LibraryPath,
    string? DataPath = null,
    string? FontConfigPath = null);

internal sealed record ActivePlaywrightComponent(
    string RootPath,
    string WorkerAssemblyPath,
    string BrowserPath,
    string LibraryPath,
    string? DataPath,
    string? FontConfigPath,
    PlaywrightComponentManifest Manifest);
