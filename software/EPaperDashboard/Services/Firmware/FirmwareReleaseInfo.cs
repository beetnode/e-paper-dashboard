namespace EPaperDashboard.Services.Firmware;

/// <summary>
/// Represents information about a firmware release from any hosting platform.
/// <paramref name="SignatureUrl"/> points to the detached signature devices verify before flashing.
/// </summary>
public sealed record FirmwareReleaseInfo(
    string Version,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAt,
    string? DownloadUrl,
    long? FileSize,
    string? SignatureUrl = null);
