using EPaperDashboard.Models;

namespace EPaperDashboard.Services;

/// <summary>
/// Decides whether a device's firmware version / last-seen timestamp needs updating on a device-port
/// request, and applies that update. A device is refreshed if its reported firmware version changed,
/// it has never been seen before, or more than a minute has passed since its last recorded check-in
/// (avoids writing to the repository on every single request from an active device).
/// </summary>
public sealed class DeviceLastSeenTracker(TimeProvider timeProvider)
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    public bool ShouldUpdate(Device device, string incomingFirmwareVersion) =>
        device.FirmwareVersion != incomingFirmwareVersion
        || device.LastSeenAt is null
        || timeProvider.GetUtcNow() - device.LastSeenAt > StaleAfter;

    /// <summary>
    /// Records a request from the device the API key belongs to. Requests without a valid key change
    /// nothing, so a device's status cannot be spoofed with just its identifier.
    /// </summary>
    public void Record(DeviceService deviceService, string? apiKey, string? firmwareVersion)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(firmwareVersion))
            return;

        var device = deviceService.GetDeviceByApiKey(apiKey);
        if (device is null || !ShouldUpdate(device, firmwareVersion))
            return;

        Apply(device, firmwareVersion);
        deviceService.UpdateDevice(device);
    }

    public void Apply(Device device, string firmwareVersion)
    {
        device.FirmwareVersion = firmwareVersion;
        device.LastSeenAt = timeProvider.GetUtcNow();
    }
}
