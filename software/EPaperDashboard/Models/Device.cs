namespace EPaperDashboard.Models;

public class Device
{
    public DeviceId Id { get; set; } = DeviceId.Empty;
    public UserId UserId { get; set; } = UserId.Empty;
    public DashboardId DashboardId { get; set; } = DashboardId.Empty;
    public string DeviceIdentifier { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>SHA-256 of the device's API key (see <see cref="Services.DeviceApiKey"/>); the key itself is never stored.</summary>
    public string ApiKeyHash { get; set; } = string.Empty;
    public DateTimeOffset PairedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public string? FirmwareVersion { get; set; }

    public int? ScreenWidth { get; set; }

    public int? ScreenHeight { get; set; }
}
