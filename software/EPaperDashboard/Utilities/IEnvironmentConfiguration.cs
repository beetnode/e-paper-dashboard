namespace EPaperDashboard.Utilities;

public interface IEnvironmentConfiguration
{
    DeploymentMode AppMode { get; }
    Uri? ClientUri { get; }
    TimeSpan DashboardScheduleCheckInterval { get; }
    TimeSpan DashboardMissedScheduleTolerance { get; }
    string ConfigDir { get; }
    string? DatabaseProvider { get; }
    string? DatabaseConnectionString { get; }
    string DataProtectionKeysDir { get; }
    bool FirmwareUpdateEnabled { get; }
    string FirmwareReleaseProvider { get; }
    string FirmwareGitHubRepo { get; }
    string FirmwareAssetPattern { get; }
    TimeSpan FirmwareCheckInterval { get; }
    /// <summary>The device port as devices reach it, after any port mapping (DEVICE_PUBLIC_PORT).</summary>
    int DevicePublicPort { get; }
}
