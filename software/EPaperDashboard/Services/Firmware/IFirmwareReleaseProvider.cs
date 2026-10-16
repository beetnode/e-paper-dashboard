namespace EPaperDashboard.Services.Firmware;

/// <summary>
/// Abstraction for fetching firmware release information from a hosting platform.
/// Implementations can target GitHub, GitLab, custom servers, or any other source.
/// </summary>
public interface IFirmwareReleaseProvider
{
    /// <summary>
    /// Gets the latest firmware release information from the hosting platform.
    /// </summary>
    Task<FirmwareReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a release asset (the firmware binary or its signature) from the given URL.
    /// </summary>
    Task<byte[]?> DownloadFirmwareBinaryAsync(string downloadUrl, CancellationToken cancellationToken = default);
}
